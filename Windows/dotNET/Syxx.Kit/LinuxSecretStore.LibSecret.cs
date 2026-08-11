using System.Runtime.InteropServices;

namespace Syxx.Kit;

public sealed partial class LinuxSecretStore
{
    // ── libsecret P/Invoke backend (primary) ──────────────────────────────────────

    /// <summary>Thin P/Invoke wrapper around libsecret functions loaded dynamically
    /// via NativeLibrary to avoid DllNotFoundException on systems without libsecret.</summary>
    internal static partial class LibSecretNative
    {
        private static IntPtr _handle;
        private static bool _available;
        private static bool _checked;

        private static IntPtr _schemaPtr;
        private static IntPtr _attrKeyTypeName;
        private static IntPtr _attrKeyIdName;

        /// <summary>libsecret <c>SecretSchema</c> attribute names. These are the strings
        /// <c>secret_attributes_buildv</c> matches the varargs NAME slots against; anything else
        /// makes it return NULL. Declared once and reused at every call site, which is the whole of
        /// the G-16 fix.</summary>
        private const string AttrKeyType = "syxx-key-type";
        private const string AttrKeyId = "syxx-key-id";

        /// <summary><c>SECRET_SEARCH_NONE</c>. Deliberately NOT <c>SECRET_SEARCH_UNLOCK</c>
        /// (1&lt;&lt;2) or <c>SECRET_SEARCH_LOAD_SECRETS</c> (1&lt;&lt;3): an existence probe must
        /// neither pop an unlock prompt nor pull secret values into this process, and a search
        /// without them still returns LOCKED items — which is exactly the case a probe must not
        /// mistake for "empty".</summary>
        private const int SecretSearchNone = 0;

        // Delegate types for libsecret functions
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool SecretPasswordStoreSync(
            IntPtr schema, IntPtr collection, IntPtr label, IntPtr password,
            IntPtr cancellable, out IntPtr error,
            IntPtr attrK1, IntPtr attrV1, IntPtr attrK2, IntPtr attrV2, IntPtr terminator);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SecretPasswordLookupSync(
            IntPtr schema, IntPtr cancellable, out IntPtr error,
            IntPtr attrK1, IntPtr attrV1, IntPtr attrK2, IntPtr attrV2, IntPtr terminator);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool SecretPasswordClearSync(
            IntPtr schema, IntPtr cancellable, out IntPtr error,
            IntPtr attrK1, IntPtr attrV1, IntPtr attrK2, IntPtr attrV2, IntPtr terminator);

        /// <summary><c>GList *secret_password_search_sync (const SecretSchema *, SecretSearchFlags,
        /// GCancellable *, GError **, ...)</c>. Three varargs slots is enough for one
        /// name/value pair plus the NULL terminator, which is all an existence probe needs — the
        /// keyId lives only in the token file that has, by hypothesis, been lost.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SecretPasswordSearchSync(
            IntPtr schema, int flags, IntPtr cancellable, out IntPtr error,
            IntPtr attrK1, IntPtr attrV1, IntPtr terminator);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GErrorFree(IntPtr error);

        /// <summary><c>secret_password_free</c>: frees AND zeroes the returned password. NOT
        /// <c>g_error_free</c>, which the previous code called on it — passing a plain string to a
        /// GError destructor reads a type tag and a domain out of the middle of the password bytes
        /// and frees whatever pointer it finds there. That is heap corruption, and it became
        /// reachable the moment G-16 was fixed and this backend started returning values.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SecretPasswordFree(IntPtr password);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GListFreeFull(IntPtr list, IntPtr freeFunc);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GListFree(IntPtr list);

        /// <summary><c>GList *secret_password_search_sync (const SecretSchema *, SecretSearchFlags,
        /// GCancellable *, GError **, ...)</c> called with NO attribute filter — just the NULL
        /// terminator. Used by the DONT_MATCH_NAME search that must find secrets regardless of which
        /// schema name they were stored under (our P/Invoke schema vs secret-tool's
        /// <c>org.freedesktop.Secret.Generic</c>).</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SecretPasswordSearchSyncUnscoped(
            IntPtr schema, int flags, IntPtr cancellable, out IntPtr error,
            IntPtr terminator);

        /// <summary><c>GHashTable *secret_retrievable_get_attributes (SecretRetrievable *)</c>.
        /// Returns a <c>(transfer full)</c> hash table the caller must unref. The table's keys and
        /// values are owned by the retrievable and must NOT be freed separately.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SecretRetrievableGetAttributes(IntPtr retrievable);

        /// <summary><c>gpointer g_hash_table_lookup (GHashTable *, gconstpointer)</c>. Returns the
        /// value stored under the key, or NULL. The returned pointer is owned by the hash table —
        /// do NOT free it.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GHashTableLookup(IntPtr hashTable, IntPtr key);

        /// <summary><c>void g_hash_table_unref (GHashTable *)</c>. Decrements the reference count
        /// of a hash table created with <c>(transfer full)</c>.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GHashTableUnref(IntPtr hashTable);

        /// <summary><c>guint g_list_length (GList *)</c>. Walks the list and returns the element
        /// count. O(n), but the list is tiny (one or two Syxx secrets at most).</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GListLength(IntPtr list);

        /// <summary><c>gpointer g_list_nth_data (GList *, guint)</c>. Returns the data pointer at
        /// position <c>n</c> (0-based), or NULL if out of range.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GListNthData(IntPtr list, uint n);

        /// <summary><c>void g_object_unref (gpointer)</c>. Decrements the reference count of a
        /// GObject. When it reaches zero the object is finalized. Each <c>SecretRetrievable</c>
        /// returned by <c>secret_password_search_sync</c> is a GObject that must be unref'd.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GObjectUnref(IntPtr obj);

        private static SecretPasswordStoreSync? _storeFunc;
        private static SecretPasswordLookupSync? _lookupFunc;
        private static SecretPasswordClearSync? _clearFunc;
        private static SecretPasswordSearchSync? _searchFunc;
        private static SecretPasswordSearchSyncUnscoped? _searchUnscopedFunc;
        private static SecretPasswordFree? _passwordFree;
        private static GErrorFree? _gErrorFree;
        private static GListFreeFull? _gListFreeFull;
        private static GListFree? _gListFree;
        private static IntPtr _gObjectUnrefPtr;
        private static GObjectUnref? _gObjectUnref;
        private static SecretRetrievableGetAttributes? _retrievableGetAttrs;
        private static GHashTableLookup? _gHashTableLookup;
        private static GHashTableUnref? _gHashTableUnref;
        private static GListLength? _gListLength;
        private static GListNthData? _gListNthData;

        /// <summary>Second schema with <c>SECRET_SCHEMA_DONT_MATCH_NAME</c> (flag 1&lt;&lt;1 = 2).
        /// Used by <see cref="SearchForKeyType"/> to find secrets regardless of which schema name
        /// they were stored under — our P/Invoke writes use <c>com.milkandgin.syxx</c>, but
        /// <c>secret-tool</c> writes use <c>org.freedesktop.Secret.Generic</c>. A schema-scoped
        /// search would miss one or the other.</summary>
        private static IntPtr _searchSchemaPtr;

        internal static bool IsAvailable
        {
            get
            {
                if (!_checked) TryLoad();
                return _available;
            }
        }

        private static IntPtr TryGetExport(IntPtr handle, string name) =>
            NativeLibrary.TryGetExport(handle, name, out var addr) ? addr : IntPtr.Zero;

        private static void TryLoad()
        {
            _checked = true;

            try
            {
                _handle = NativeLibrary.Load("libsecret-1.so.0");

                // Resolve function pointers
                _storeFunc = Marshal.GetDelegateForFunctionPointer<SecretPasswordStoreSync>(
                    NativeLibrary.GetExport(_handle, "secret_password_store_sync"));
                _lookupFunc = Marshal.GetDelegateForFunctionPointer<SecretPasswordLookupSync>(
                    NativeLibrary.GetExport(_handle, "secret_password_lookup_sync"));
                _clearFunc = Marshal.GetDelegateForFunctionPointer<SecretPasswordClearSync>(
                    NativeLibrary.GetExport(_handle, "secret_password_clear_sync"));

                var freePtr = TryGetExport(_handle, "secret_password_free");
                if (freePtr != IntPtr.Zero)
                    _passwordFree = Marshal.GetDelegateForFunctionPointer<SecretPasswordFree>(freePtr);

                // secret_password_search_sync arrived in libsecret 0.19. Its absence is not an error
                // — the store simply keeps its pre-existing inability to answer Exists for this
                // backend, and Exists reports Unknown rather than claiming Absent.
                var searchPtr = TryGetExport(_handle, "secret_password_search_sync");
                if (searchPtr != IntPtr.Zero)
                {
                    _searchFunc = Marshal.GetDelegateForFunctionPointer<SecretPasswordSearchSync>(searchPtr);
                    // Same function, different varargs shape: the unscoped delegate passes only the
                    // NULL terminator (no attribute name/value pairs), for the DONT_MATCH_NAME search
                    // that must find secrets regardless of schema name.
                    _searchUnscopedFunc = Marshal.GetDelegateForFunctionPointer<SecretPasswordSearchSyncUnscoped>(searchPtr);
                }

                // secret_retrievable_get_attributes: needed to inspect each search result's attributes
                // and determine whether a SecretRetrievable is a witness or licence secret.
                var retrievableAttrsPtr = TryGetExport(_handle, "secret_retrievable_get_attributes");
                if (retrievableAttrsPtr != IntPtr.Zero)
                    _retrievableGetAttrs = Marshal.GetDelegateForFunctionPointer<SecretRetrievableGetAttributes>(retrievableAttrsPtr);

                // glib/gobject: resolvable through the libsecret handle because dlsym searches a
                // library's dependency chain; libglib-2.0.so.0 is the explicit fallback. The previous
                // code asked for "glib-2.0.so.0", which NativeLibrary.Load does not decorate with a
                // "lib" prefix, so that fallback could never have loaded.
                var errFreePtr = TryGetExport(_handle, "g_error_free");
                var listFreeFullPtr = TryGetExport(_handle, "g_list_free_full");
                var listFreePtr = TryGetExport(_handle, "g_list_free");
                var hashLookupPtr = TryGetExport(_handle, "g_hash_table_lookup");
                var hashUnrefPtr = TryGetExport(_handle, "g_hash_table_unref");
                var listLengthPtr = TryGetExport(_handle, "g_list_length");
                var listNthDataPtr = TryGetExport(_handle, "g_list_nth_data");
                _gObjectUnrefPtr = TryGetExport(_handle, "g_object_unref");

                if (errFreePtr == IntPtr.Zero || listFreeFullPtr == IntPtr.Zero
                    || hashLookupPtr == IntPtr.Zero || listLengthPtr == IntPtr.Zero)
                {
                    try
                    {
                        var glib = NativeLibrary.Load("libglib-2.0.so.0");
                        if (errFreePtr == IntPtr.Zero) errFreePtr = TryGetExport(glib, "g_error_free");
                        if (listFreeFullPtr == IntPtr.Zero) listFreeFullPtr = TryGetExport(glib, "g_list_free_full");
                        if (listFreePtr == IntPtr.Zero) listFreePtr = TryGetExport(glib, "g_list_free");
                        if (hashLookupPtr == IntPtr.Zero) hashLookupPtr = TryGetExport(glib, "g_hash_table_lookup");
                        if (hashUnrefPtr == IntPtr.Zero) hashUnrefPtr = TryGetExport(glib, "g_hash_table_unref");
                        if (listLengthPtr == IntPtr.Zero) listLengthPtr = TryGetExport(glib, "g_list_length");
                        if (listNthDataPtr == IntPtr.Zero) listNthDataPtr = TryGetExport(glib, "g_list_nth_data");
                    }
                    catch { /* glib not separately loadable; the resolved subset still works */ }
                }
                if (_gObjectUnrefPtr == IntPtr.Zero)
                {
                    try { _gObjectUnrefPtr = TryGetExport(NativeLibrary.Load("libgobject-2.0.so.0"), "g_object_unref"); }
                    catch { /* list entries will be freed without unref-ing the items */ }
                }

                if (errFreePtr != IntPtr.Zero)
                    _gErrorFree = Marshal.GetDelegateForFunctionPointer<GErrorFree>(errFreePtr);
                if (listFreeFullPtr != IntPtr.Zero)
                    _gListFreeFull = Marshal.GetDelegateForFunctionPointer<GListFreeFull>(listFreeFullPtr);
                if (listFreePtr != IntPtr.Zero)
                    _gListFree = Marshal.GetDelegateForFunctionPointer<GListFree>(listFreePtr);
                if (hashLookupPtr != IntPtr.Zero)
                    _gHashTableLookup = Marshal.GetDelegateForFunctionPointer<GHashTableLookup>(hashLookupPtr);
                if (hashUnrefPtr != IntPtr.Zero)
                    _gHashTableUnref = Marshal.GetDelegateForFunctionPointer<GHashTableUnref>(hashUnrefPtr);
                if (listLengthPtr != IntPtr.Zero)
                    _gListLength = Marshal.GetDelegateForFunctionPointer<GListLength>(listLengthPtr);
                if (listNthDataPtr != IntPtr.Zero)
                    _gListNthData = Marshal.GetDelegateForFunctionPointer<GListNthData>(listNthDataPtr);
                if (_gObjectUnrefPtr != IntPtr.Zero)
                    _gObjectUnref = Marshal.GetDelegateForFunctionPointer<GObjectUnref>(_gObjectUnrefPtr);

                // Build the SecretSchema as a pinned memory block.
                // Schema layout (see libsecret/secret-schema.h):
                //   [name_ptr:8][flags:4][padding:4]
                //   [attr0_name:8][attr0_type:4][padding:4]
                //   [attr1_name:8][attr1_type:4][padding:4]
                //   [attr2_name:8][attr2_type:4][padding:4]
                //   [attr3_name:8][attr3_type:4][padding:4]  -- sentinel (name=null)
                var schemaName = Marshal.StringToCoTaskMemUTF8("com.milkandgin.syxx");
                _attrKeyTypeName = Marshal.StringToCoTaskMemUTF8(AttrKeyType);
                _attrKeyIdName = Marshal.StringToCoTaskMemUTF8(AttrKeyId);
                const int SecretSchemaAttributeString = 0;

                var schemaBytes = new byte[64]; // 5 entries * 12 bytes + alignment padding
                var schemaHandle = GCHandle.Alloc(schemaBytes, GCHandleType.Pinned);
                _schemaPtr = schemaHandle.AddrOfPinnedObject();

                // Write schema_name pointer
                Marshal.WriteIntPtr(_schemaPtr, 0, schemaName);
                // flags = 0 (SECRET_SCHEMA_NONE)
                Marshal.WriteInt32(_schemaPtr, 8, 0);
                // attr0: name, type
                Marshal.WriteIntPtr(_schemaPtr, 16, _attrKeyTypeName);
                Marshal.WriteInt32(_schemaPtr, 24, SecretSchemaAttributeString);
                // attr1: name, type
                Marshal.WriteIntPtr(_schemaPtr, 32, _attrKeyIdName);
                Marshal.WriteInt32(_schemaPtr, 40, SecretSchemaAttributeString);
                // attr2-4: sentinel (null name, zero type) -- already zeroed

                // Build a SECOND schema for the DONT_MATCH_NAME search. Same attribute definitions,
                // but the flag tells libsecret to ignore the schema name when matching. This is
                // required because secret-tool stores items with xdg:schema =
                // org.freedesktop.Secret.Generic, while our P/Invoke writes use
                // com.milkandgin.syxx. A schema-scoped search (flag 0) would only find one set.
                const int SecretSchemaDontMatchName = 1 << 1; // = 2
                var searchSchemaBytes = new byte[64];
                var searchSchemaHandle = GCHandle.Alloc(searchSchemaBytes, GCHandleType.Pinned);
                _searchSchemaPtr = searchSchemaHandle.AddrOfPinnedObject();

                Marshal.WriteIntPtr(_searchSchemaPtr, 0, schemaName);   // name is ignored by the flag
                Marshal.WriteInt32(_searchSchemaPtr, 8, SecretSchemaDontMatchName);
                Marshal.WriteIntPtr(_searchSchemaPtr, 16, _attrKeyTypeName);
                Marshal.WriteInt32(_searchSchemaPtr, 24, SecretSchemaAttributeString);
                Marshal.WriteIntPtr(_searchSchemaPtr, 32, _attrKeyIdName);
                Marshal.WriteInt32(_searchSchemaPtr, 40, SecretSchemaAttributeString);
                // sentinel: already zeroed

                _available = true;
            }
            catch
            {
                // libsecret not available; caller falls back to secret-tool or file store
                _available = false;
            }
        }

        /// <summary>
        /// G-16. The varargs of <c>secret_password_store_sync</c> are ATTRIBUTE NAME / ATTRIBUTE
        /// VALUE pairs terminated by NULL. The previous call passed the keyType VALUE ("witness")
        /// into the first NAME slot and the keyId VALUE into the first VALUE slot, so libsecret was
        /// asked for an attribute literally named "witness". No such attribute is declared in the
        /// schema, <c>secret_attributes_buildv</c> answered with <c>g_critical</c> + NULL, and
        /// <c>secret_password_store_sync</c>'s "<c>if (!attributes) return FALSE;</c>" returned FALSE
        /// WITHOUT setting the GError. <see cref="Store"/> read that as "backend declined" and fell
        /// through, so nothing was ever persisted by this route.
        ///
        /// <para>The fix is the slot alignment below: name, value, name, value, NULL.</para>
        ///
        /// <para>EVIDENCE BOUNDARY. Read from libsecret master's <c>secret-password.c</c> and
        /// <c>secret-attributes.c</c>. NOT verified against 0.18 / 0.20 or distro-patched builds, and
        /// not executed at all — there is no libsecret on the authoring host. The claim is "this call
        /// could not have stored on the sources read", not "never, on every machine".</para>
        /// </summary>
        internal static bool Store(string keyType, string keyId, string secret)
        {
            if (!IsAvailable || _storeFunc is null) return false;

            try
            {
                var labelPtr = Marshal.StringToCoTaskMemUTF8($"Syxx {keyType}");
                var passwordPtr = Marshal.StringToCoTaskMemUTF8(secret);
                var keyTypePtr = Marshal.StringToCoTaskMemUTF8(keyType);
                var keyIdPtr = Marshal.StringToCoTaskMemUTF8(keyId);

                try
                {
                    bool result = _storeFunc(
                        _schemaPtr,
                        IntPtr.Zero,        // collection: NULL = default
                        labelPtr,
                        passwordPtr,
                        IntPtr.Zero,        // cancellable: NULL (synchronous)
                        out var error,
                        _attrKeyTypeName, keyTypePtr,   // attribute NAME, attribute VALUE
                        _attrKeyIdName, keyIdPtr,       // attribute NAME, attribute VALUE
                        IntPtr.Zero);                   // varargs terminator

                    if (!result && error != IntPtr.Zero)
                        _gErrorFree?.Invoke(error);

                    return result;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(labelPtr);
                    Marshal.FreeCoTaskMem(passwordPtr);
                    Marshal.FreeCoTaskMem(keyTypePtr);
                    Marshal.FreeCoTaskMem(keyIdPtr);
                }
            }
            catch
            {
                return false;
            }
        }

        internal static string? Retrieve(string keyType, string keyId)
        {
            if (!IsAvailable || _lookupFunc is null) return null;

            try
            {
                var keyTypePtr = Marshal.StringToCoTaskMemUTF8(keyType);
                var keyIdPtr = Marshal.StringToCoTaskMemUTF8(keyId);

                try
                {
                    IntPtr resultPtr = _lookupFunc(
                        _schemaPtr,
                        IntPtr.Zero,        // cancellable
                        out var error,
                        _attrKeyTypeName, keyTypePtr,   // attribute NAME, attribute VALUE
                        _attrKeyIdName, keyIdPtr,       // attribute NAME, attribute VALUE
                        IntPtr.Zero);                   // varargs terminator

                    if (resultPtr != IntPtr.Zero)
                    {
                        var result = Marshal.PtrToStringUTF8(resultPtr);
                        // secret_password_free zeroes the buffer before releasing it. If it could not
                        // be resolved we leak rather than guess at a destructor — a wrong free on a
                        // secret is worse than a leaked page.
                        _passwordFree?.Invoke(resultPtr);
                        return result;
                    }

                    if (error != IntPtr.Zero)
                        _gErrorFree?.Invoke(error);

                    return null;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(keyTypePtr);
                    Marshal.FreeCoTaskMem(keyIdPtr);
                }
            }
            catch
            {
                return null;
            }
        }

        internal static void Delete(string keyType, string keyId)
        {
            if (!IsAvailable || _clearFunc is null) return;

            try
            {
                var keyTypePtr = Marshal.StringToCoTaskMemUTF8(keyType);
                var keyIdPtr = Marshal.StringToCoTaskMemUTF8(keyId);

                try
                {
                    _clearFunc(
                        _schemaPtr,
                        IntPtr.Zero,        // cancellable
                        out var error,
                        _attrKeyTypeName, keyTypePtr,   // attribute NAME, attribute VALUE
                        _attrKeyIdName, keyIdPtr,       // attribute NAME, attribute VALUE
                        IntPtr.Zero);                   // varargs terminator

                    if (error != IntPtr.Zero)
                        _gErrorFree?.Invoke(error);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(keyTypePtr);
                    Marshal.FreeCoTaskMem(keyIdPtr);
                }
            }
            catch
            {
                // best-effort
            }
        }
    }
}
