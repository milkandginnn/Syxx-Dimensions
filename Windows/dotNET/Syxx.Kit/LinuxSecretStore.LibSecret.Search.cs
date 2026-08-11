using System.Runtime.InteropServices;

namespace Syxx.Kit;

public sealed partial class LinuxSecretStore
{
    internal static partial class LibSecretNative
    {
        private static bool? _searchUsable;

        /// <summary>
        /// Whether the search binding may be believed.
        ///
        /// <para>WHY THIS EXISTS. Nothing in this P/Invoke layer has ever been executed against a
        /// real libsecret — there is none on the authoring host — and the answer this binding
        /// produces is consumed by the mint guard. A false <b>Unknown</b> is harmless: the caller
        /// already fails open on Unknown. A false <b>Present</b> is not: it would tell every
        /// first-run GNOME/KDE machine that an identity already exists, leaving no route to a first
        /// key at all. Believing an unverified binding in that direction is exactly the "the artifact
        /// exists, so it must be in force" reasoning this codebase keeps getting wrong.</para>
        ///
        /// <para>So the binding validates itself at the point of use: it searches for a keyType that
        /// cannot exist. A correct binding returns an empty list and no error. Anything else — a hit,
        /// an error, a throw — means the call is not behaving as documented, the capability is
        /// discarded, and this backend goes back to answering Unknown.</para>
        /// </summary>
        private static bool SearchCapabilityUsable()
        {
            if (_searchUsable.HasValue) return _searchUsable.Value;

            var control = "syxx-control-" + Guid.NewGuid().ToString("N");
            var outcome = RawSearch(control);
            _searchUsable = outcome == RawSearchOutcome.Empty;
            if (_searchUsable == false)
            {
                // E016: breadcrumb wrapped in try/catch — must not mask the search-capability result.
                try
                {
                    SyxxDiagnostics.Breadcrumb(
                        category: "keystore.libsecret",
                        message: "libsecret search binding failed its control probe and will not be used "
                               + "to answer key presence; this backend reports Unknown instead.",
                        level: DiagnosticSeverity.Warning,
                        data: new Dictionary<string, string> { ["outcome"] = outcome.ToString() });
                }
                catch { /* E016: breadcrumb infrastructure must not replace the capability probe result */ }
            }
            return _searchUsable.Value;
        }

        private enum RawSearchOutcome { Empty, Found, Failed }

        private static RawSearchOutcome RawSearch(string keyType)
        {
            if (_searchFunc is null) return RawSearchOutcome.Failed;

            try
            {
                var keyTypePtr = Marshal.StringToCoTaskMemUTF8(keyType);
                try
                {
                    IntPtr list = _searchFunc(
                        _schemaPtr,
                        SecretSearchNone,
                        IntPtr.Zero,                    // cancellable
                        out var error,
                        _attrKeyTypeName, keyTypePtr,   // attribute NAME, attribute VALUE
                        IntPtr.Zero);                   // varargs terminator

                    if (error != IntPtr.Zero)
                    {
                        _gErrorFree?.Invoke(error);
                        if (list != IntPtr.Zero) FreeList(list);
                        return RawSearchOutcome.Failed;
                    }

                    if (list == IntPtr.Zero) return RawSearchOutcome.Empty;

                    FreeList(list);
                    return RawSearchOutcome.Found;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(keyTypePtr);
                }
            }
            catch
            {
                return RawSearchOutcome.Failed;
            }
        }

        private static void FreeList(IntPtr list)
        {
            try
            {
                if (_gListFreeFull is not null && _gObjectUnrefPtr != IntPtr.Zero)
                    _gListFreeFull(list, _gObjectUnrefPtr);
                else
                    _gListFree?.Invoke(list);
            }
            catch { /* a leaked list must not take the probe down */ }
        }

        /// <summary>G-02b: does the keyring hold any secret filed under this keyType, asked without a
        /// keyId and without the Secret Service being required to unlock anything? Unknown whenever
        /// the search is unavailable, unvalidated, or reported an error — never Absent on a backend
        /// we could not actually interrogate.
        ///
        /// <para>This is the SCHEMA-SCOPED search: it uses the <c>com.milkandgin.syxx</c> schema and
        /// therefore finds only secrets stored through our P/Invoke backend. Secrets stored by
        /// <c>secret-tool</c> (which uses <c>org.freedesktop.Secret.Generic</c>) are invisible to
        /// this search. For a search that finds both, use <see cref="SearchForKeyType"/>.</para>
        /// </summary>
        internal static SecretExistence Exists(string keyType)
        {
            if (!IsAvailable || _searchFunc is null) return SecretExistence.Unknown;
            if (!SearchCapabilityUsable()) return SecretExistence.Unknown;

            return RawSearch(keyType) switch
            {
                RawSearchOutcome.Found => SecretExistence.Present,
                RawSearchOutcome.Empty => SecretExistence.Absent,
                _ => SecretExistence.Unknown,
            };
        }

        /// <summary>
        /// G-16 + G-02b: searches the keyring for any secret carrying
        /// <c>syxx-key-type = <paramref name="keyType"/></c>, regardless of which schema name it was
        /// stored under. Uses <c>SECRET_SCHEMA_DONT_MATCH_NAME</c> so that both P/Invoke-stored
        /// secrets (<c>com.milkandgin.syxx</c>) and secret-tool-stored secrets
        /// (<c>org.freedesktop.Secret.Generic</c>) are visible.
        ///
        /// <para>WHY DONT_MATCH_NAME IS NEEDED. <c>secret-tool store</c> files items under the
        /// freedesktop standard schema <c>org.freedesktop.Secret.Generic</c>. Our P/Invoke backend
        /// files them under <c>com.milkandgin.syxx</c>. A schema-scoped search (the existing
        /// <see cref="Exists"/>) matches only one. On a machine where the witness secret was stored
        /// by secret-tool and the token file was subsequently lost, the scoped search returns empty
        /// and the mint guard fires — overwriting a live identity. The DONT_MATCH_NAME flag tells
        /// libsecret to ignore the schema name and match on attributes alone, closing that gap.</para>
        ///
        /// <para>WHY THE GLIST WALK. An unscoped search (no attribute filter in varargs) would
        /// return ALL secrets in the collection — witness, licence, and anything else any application
        /// ever stored. The <c>syxx-key-type</c> attribute filter in the varargs narrows it to Syxx
        /// secrets, but the client-side walk verifies each result's <c>syxx-key-type</c> attribute
        /// matches the requested keyType. Without this check, a machine holding only a licence
        /// secret would incorrectly report <see cref="SecretExistence.Present"/> for a witness
        /// probe, blocking first-run minting.</para>
        ///
        /// <para>MEMORY CONTRACT. <c>secret_password_search_sync</c> returns a <c>GList</c> of
        /// <c>SecretRetrievable*</c> (transfer full, elements transfer full). Each element is a
        /// GObject that must be <c>g_object_unref</c>'d. The list itself is freed with
        /// <c>g_list_free</c>. <c>secret_retrievable_get_attributes</c> returns a
        /// <c>(transfer full)</c> <c>GHashTable</c> that must be <c>g_hash_table_unref</c>'d.
        /// The hash table's keys and values are owned by the retrievable and must NOT be freed.
        /// Every exit path — normal, early return, exception — cleans up in a <c>finally</c>.</para>
        ///
        /// <para>SEARCH FLAGS. <c>SECRET_SEARCH_NONE</c> (0): does NOT request unlock and does NOT
        /// load secret values. A locked but populated collection still answers
        /// <see cref="SecretExistence.Present"/> — which is exactly the case this probe must not
        /// mistake for empty.</para>
        /// </summary>
        internal static SecretExistence SearchForKeyType(string keyType)
        {
            // Guard: all four bindings needed for the walk must be resolvable.
            if (!IsAvailable || _searchUnscopedFunc is null) return SecretExistence.Unknown;
            if (!SearchCapabilityUsable()) return SecretExistence.Unknown;
            if (_retrievableGetAttrs is null || _gHashTableLookup is null
                || _gListLength is null || _gListNthData is null)
                return SecretExistence.Unknown;

            IntPtr list = IntPtr.Zero;
            try
            {
                list = _searchUnscopedFunc(
                    _searchSchemaPtr,       // DONT_MATCH_NAME schema
                    SecretSearchNone,       // no unlock, no value load
                    IntPtr.Zero,            // cancellable: NULL (synchronous)
                    out var error,
                    IntPtr.Zero);           // varargs terminator — unscoped search

                if (error != IntPtr.Zero)
                {
                    _gErrorFree?.Invoke(error);
                    return SecretExistence.Unknown;
                }

                if (list == IntPtr.Zero)
                    return SecretExistence.Absent;

                // Walk the GList. For each SecretRetrievable, inspect its attributes to determine
                // whether it carries the requested keyType. The search returned ALL items matching
                // the DONT_MATCH_NAME schema (i.e., all items with our declared attributes), so the
                // walk MUST filter by the syxx-key-type value to avoid conflating witness with
                // licence secrets.
                uint count = _gListLength(list);
                if (count == 0)
                    return SecretExistence.Absent;

                var keyTypeNamePtr = Marshal.StringToCoTaskMemUTF8(AttrKeyType);
                try
                {
                    bool found = false;
                    for (uint i = 0; i < count; i++)
                    {
                        IntPtr retrievable = _gListNthData(list, i);
                        if (retrievable == IntPtr.Zero) continue;

                        IntPtr attrs = IntPtr.Zero;
                        try
                        {
                            attrs = _retrievableGetAttrs(retrievable);
                            if (attrs == IntPtr.Zero) continue;

                            IntPtr valuePtr = _gHashTableLookup(attrs, keyTypeNamePtr);
                            if (valuePtr == IntPtr.Zero) continue;

                            var value = Marshal.PtrToStringUTF8(valuePtr);
                            if (string.Equals(value, keyType, StringComparison.Ordinal))
                            {
                                found = true;
                                break;
                            }
                        }
                        finally
                        {
                            // secret_retrievable_get_attributes returns (transfer full) — must unref.
                            if (attrs != IntPtr.Zero)
                                _gHashTableUnref?.Invoke(attrs);
                        }
                    }

                    return found ? SecretExistence.Present : SecretExistence.Absent;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(keyTypeNamePtr);
                }
            }
            catch
            {
                return SecretExistence.Unknown;
            }
            finally
            {
                // Free the GList and all its SecretRetrievable elements. FreeList uses
                // g_list_free_full(list, g_object_unref) when both are available, which unrefs every
                // element and frees the list structure in one call. Falls back to g_list_free (list
                // structure only) if g_object_unref was not resolvable — elements leak, but the
                // probe does not crash.
                if (list != IntPtr.Zero)
                    FreeList(list);
            }
        }
    }
}
