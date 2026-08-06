import SwiftUI
import SyxxKit

struct WitnessView: View {
    @EnvironmentObject var model: AppModel
    /// View-local only — the plaintext signing key is never stored on the model.
    /// It is routed into the Keychain via `model.saveWitnessKey` on submit.
    @State private var witnessKeyInput: String = ""

    /// M-01: which action was interrupted by the overwrite confirmation, so confirming resumes
    /// exactly what the user asked for and cancelling resumes nothing. Nothing has been written to
    /// the Keychain while this is non-nil.
    private enum PendingKeyWrite { case testify, verify, generate }
    @State private var pendingKeyWrite: PendingKeyWrite? = nil

    /// The ONE honest result rendered in the validation report (D069/B049/E021). Icon/color/copy
    /// are driven entirely by `model.witnessResult`; there is no unconditional "valid" row.
    /// Every forensic failure reason is distinct so expired honest snapshots are never confused
    /// with forged ones.
    private var witnessResultDisplay: (icon: String, color: Color, text: String) {
        switch model.witnessResult {
        case .valid:
            // B053: split signature integrity from the snapshot's own network result.
            // "Signature matches" is an Ed25519 fact; "no tampering" was a false-clean
            // NETWORK claim driven solely by an intact signature.
            return ("checkmark.seal.fill", .ioSuccessGreen,
                    "Signature intact — snapshot is authentic. Review the snapshot's own result for tampering findings.")
        case .expired:
            return ("xmark.seal.fill", .ioWarningAmber,
                    "Signature valid but the snapshot has expired — the evidence is genuine but no longer current.")
        case .stale:
            return ("xmark.seal.fill", .ioWarningAmber,
                    "Signature valid but the snapshot is too old — evidence freshness threshold exceeded.")
        case .futureDated:
            return ("xmark.seal.fill", .ioCriticalRed,
                    "Signature valid but the timestamp is future-dated — possible clock manipulation or forged evidence.")
        case .untrustedSigner:
            return ("xmark.seal.fill", .ioCriticalRed,
                    "Signature valid but the signer is not in your trust set — evidence may be authentic but is unattested.")
        case .displayMismatch:
            return ("xmark.seal.fill", .ioCriticalRed,
                    "Signature valid but display fields do not match the signed evidence — presentation was tampered.")
        case .invalidSignature:
            return ("xmark.seal.fill", .ioCriticalRed,
                    "Testimony invalid — signature mismatch; the snapshot was altered after signing.")
        case .malformedInput:
            return ("xmark.seal.fill", .ioCriticalRed,
                    "Cannot verify — the snapshot is malformed or the public key is missing.")
        case .unsupportedFormat:
            return ("xmark.seal.fill", .ioCriticalRed,
                    "Cannot verify — the snapshot format version is not supported by this verifier.")
        case .noKey(.confirmedAbsent):
            // The Keychain gave a real answer (errSecItemNotFound). Inviting the user to enter a
            // key is correct here and destroys nothing, because there is nothing to destroy.
            return ("xmark.seal.fill", .ioWarningAmber,
                    "Cannot verify — no signing key on this machine. Enter your key above, then verify again.")
        case .noKey(.undetermined):
            // M-01: missing data, not an answer. This arm deliberately carries NO "enter your key
            // above" instruction: following it would overwrite a key that probably still exists,
            // and every testimony already sealed with it would stop verifying.
            return ("questionmark.circle.fill", .ioWarningAmber,
                    "Cannot verify — the Keychain could not be read, so whether a signing key is present here could not be determined. This is not the same as having no key; do not enter a new one yet. Unlock the Keychain and verify again.")
        case .notVerified:
            return ("xmark.seal.fill", .ioWarningAmber,
                    "No signed snapshot available for comparison — press Witness Testimony first.")
        }
    }

    var body: some View {
        PurpleScrollView(geometry: .fullPanel) {
            VStack(alignment: .leading, spacing: 24) {
                // Warden header: rune tile + eyebrow + name + lore
                HStack(alignment: .top, spacing: 15) {
                    Text("\u{16C9}")
                        .font(.custom("Cinzel-SemiBold", size: 28))
                        .frame(width: 54, height: 54)
                        .background(Color.ioWitnessRed.opacity(0.10))
                        .cornerRadius(14)
                        .foregroundColor(Color.ioWitnessRed)
                        .shadow(color: Color.ioWitnessRed.opacity(0.35), radius: 22)
                        .accessibilityHidden(true)

                    VStack(alignment: .leading, spacing: 4) {
                        Text("THE SEALBEARER \u{00B7} ENVIRONMENT TRUST")
                            .font(.ioCaption(.bold))
                            .foregroundColor(Color.ioWitnessRed)
                            .tracking(0.26)

                        Text("Witness")
                            .font(.custom("Cinzel-SemiBold", size: 26))
                            .foregroundColor(.ioText)

                        Text("And the Witness testified: when local and remote records matched, when they held steady over time, and when no leak betrayed the encrypted path, the DNS was healthy. When they did not, the network, and its eyes, were lying indeed.")
                            .font(.ioLore(16))
                            .foregroundColor(.ioTextMuted)
                            .fixedSize(horizontal: false, vertical: true)
                            .frame(maxWidth: 560)
                    }
                }
                
                // Form Fields
                VStack(alignment: .leading, spacing: 14) {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("What domain would you like to check?")
                            .font(.ioCaption())
                            .foregroundColor(.ioTextSecondary)
                        Text("Which name shall the Witness set down as testimony? The Witness resolves this domain and seals the result, so its truth can be proven in days to come. (The record is signed with an Ed25519 key. Should even a single character be altered afterward, the seal breaks and the tampering is revealed.)")
                            .font(.ioSmall())
                            .foregroundColor(.ioTextSecondary)
                            .fixedSize(horizontal: false, vertical: true)
                        TextField("Domain (e.g., github.com)", text: $model.domain)
                            .textFieldStyle(.roundedBorder)
                    }

                    VStack(alignment: .leading, spacing: 6) {
                        Text("Provide the Ed25519 signing key (base64) that seals your testimony — paste an existing key, or click Generate Keypair below to create one:")
                            .font(.ioCaption())
                            .foregroundColor(.ioTextSecondary)
                        Text("Entrust the Witness with the private key by which your testimony is sealed. It rests upon this machine alone and is never sent abroad; only the seal it makes travels with your evidence. (Whoever holds this key could forge testimony in your name. Guard it well.)")
                            .font(.ioSmall())
                            .foregroundColor(.ioTextSecondary)
                            .fixedSize(horizontal: false, vertical: true)
                        SecureField("Ed25519 private key (base64)…", text: $witnessKeyInput)
                            .textFieldStyle(.roundedBorder)
                    }
                    
                    HStack(spacing: 12) {
                        Button(action: {
                            // Only proceed (and clear the field) if the key actually saved. On a
                            // Keychain failure saveWitnessKey surfaces the error and returns false;
                            // we keep the typed key so the user can retry (D052). M-01: it also
                            // returns false when a key already exists — the confirmation below then
                            // resumes this action, or drops it.
                            pendingKeyWrite = .testify
                            if model.saveWitnessKey(witnessKeyInput) {
                                pendingKeyWrite = nil
                                witnessKeyInput = ""   // don't let the plaintext key linger in view state
                                model.witnessTestify()
                            }
                        }) {
                            Text("Witness Testimony")
                        }
                        .buttonStyle(IoFilledButtonStyle())

                        Button(action: {
                            pendingKeyWrite = .verify
                            if model.saveWitnessKey(witnessKeyInput) {
                                pendingKeyWrite = nil
                                witnessKeyInput = ""
                                model.witnessVerify()
                            }
                        }) {
                            Text("Verify Testimony")
                        }
                        .buttonStyle(IoOutlineButtonStyle())

                        // E045: generate a new Ed25519 keypair and store it in the Keychain.
                        // Previously WitnessKeyManager.generateAndStoreKey() existed but was
                        // never exposed to the GUI, forcing users to CLI or external tools.
                        Button(action: {
                            pendingKeyWrite = .generate
                            if model.generateWitnessKey() {
                                pendingKeyWrite = nil
                                // A fresh keypair is generated and stored directly in the Keychain;
                                // clear any stale typed text so the next "Witness Testimony" doesn't
                                // re-validate it and fail while a valid key is already stored.
                                witnessKeyInput = ""
                            }
                        }) {
                            Text("Generate Keypair")
                        }
                        .buttonStyle(IoOutlineButtonStyle())
                    }
                }
                .frame(maxWidth: 640)
                .padding(18)
                .background(Color.ioSurfaceDark)
                .cornerRadius(12)
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBorderSubtle, lineWidth: 1))

                // 5(b): verify a testimony sealed on ANOTHER machine. Distinct from the self-verify
                // above — no local key is required; the paste is checked against your witness trust
                // set. Malformed JSON renders an honest failure, never a crash.
                VStack(alignment: .leading, spacing: 6) {
                    Text("Verify a testimony from another machine")
                        .font(.ioCaption())
                        .foregroundColor(.ioTextSecondary)
                    Text("Paste a signed testimony sealed elsewhere. Its Ed25519 seal is checked against the witness keys you trust; nothing here is signed or stored. If the text is not a valid signed testimony, the report says so plainly.")
                        .font(.ioSmall())
                        .foregroundColor(.ioTextSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                    ZStack(alignment: .topLeading) {
                        if model.externalSnapshotJson.isEmpty {
                            Text("Paste a signed testimony (JSON)…")
                                .font(.ioMono(13))
                                .foregroundColor(.ioTextMuted)
                                .padding(.horizontal, 5)
                                .padding(.vertical, 8)
                                .allowsHitTesting(false)
                        }
                        TextEditor(text: $model.externalSnapshotJson)
                            .font(.ioMono(13))
                            .frame(minHeight: 96)
                            .scrollContentBackground(.hidden)
                            .background(Color.ioBackgroundDark)
                    }
                    .background(Color.ioBackgroundDark)
                    .cornerRadius(8)
                    .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.ioBorderSubtle, lineWidth: 1))

                    Button(action: {
                        model.witnessVerifyExternal()
                    }) {
                        Text("Verify pasted testimony")
                    }
                    .buttonStyle(IoOutlineButtonStyle())
                    .padding(.top, 4)
                }
                .frame(maxWidth: 640)
                .padding(18)
                .background(Color.ioSurfaceDark)
                .cornerRadius(12)
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBorderSubtle, lineWidth: 1))

                // Testimony Output
                if model.showTestimony {
                    VStack(alignment: .leading, spacing: 12) {
                        if case .failed(let reason) = model.witnessTestimonyState {
                            // B195: a dead/aborted sealing probe renders as a distinct, clearly-failed
                            // row — never the neutral "Not yet signed" placeholder that a still-running
                            // probe shows. Amber caution (operational failure, not a tamper accusation),
                            // matching the notVerified/noKey vocabulary of the validation result.
                            HStack(alignment: .top, spacing: 8) {
                                Image(systemName: "xmark.seal.fill")
                                    .foregroundColor(.ioWarningAmber)
                                Text(reason)
                                    .font(.ioBody())
                                    .foregroundColor(.ioWarningAmber)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            .padding(12)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(Color.ioBackgroundDark)
                            .cornerRadius(8)
                            .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.ioBorderSubtle, lineWidth: 1))
                        } else {
                            Text("Creating signed Witness Testimony for ")
                                .font(.ioBody())
                                .foregroundColor(.ioText)
                            + Text(model.domain.isEmpty ? "(no domain entered)" : model.domain)
                                .font(.ioMonoBold(14))
                                .foregroundColor(model.domain.isEmpty ? .ioTextMuted : .ioElectric)
                            + Text(" via ")
                            + Text(model.cachedSystemDNSServers.first(where: { !$0.isEmpty }) ?? "resolver not determined")
                                .font(.ioMonoBold(14))
                                .foregroundColor(model.cachedSystemDNSServers.isEmpty ? .ioTextMuted : .ioElectric)

                            VStack(alignment: .leading, spacing: 6) {
                                HStack {
                                    Text("Snapshot signature")
                                        .font(.ioCaption(.bold))
                                        .foregroundColor(.ioTextSecondary)
                                        .textCase(.uppercase)
                                    Spacer()
                                }
                                Text(model.witnessSignature.isEmpty ? "Not yet signed" : model.witnessSignature)
                                    .font(.ioMono(14))
                                    .foregroundColor(.ioTextSecondary)
                                    .lineLimit(nil)
                            }
                            .padding(12)
                            .background(Color.ioBackgroundDark)
                            .cornerRadius(8)
                            .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.ioBorderSubtle, lineWidth: 1))
                        }
                    }
                    .padding(18)
                    .frame(maxWidth: 820, alignment: .leading)
                    .background(Color.ioWitnessRed.opacity(0.05))
                    .background(Color.ioSurfaceDark)
                    .cornerRadius(12)
                    .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioWitnessRed.opacity(0.15), lineWidth: 1))
                }
                
                // Validation Report
                if model.showValidation {
                    VStack(alignment: .leading, spacing: 14) {
                        Text("Testimony Validation Report:")
                            .font(.ioCaption(.bold))
                            .foregroundColor(.ioTextSecondary)
                            .textCase(.uppercase)

                        if let snap = model.witnessSnapshot {
                            // A snapshot exists — show its metadata, the signature on file, and a
                            // SINGLE honest result row. No unconditional "valid" row (D069/B049).
                            VStack(alignment: .leading, spacing: 6) {
                                HStack(spacing: 12) {
                                    Text("Domain:").font(.ioBody()).foregroundColor(.ioTextSecondary)
                                    Text(snap.targetDomain).font(.ioMono(14)).foregroundColor(.ioText)
                                }
                                HStack(spacing: 12) {
                                    Text("Timestamp:").font(.ioBody()).foregroundColor(.ioTextSecondary)
                                    Text(snap.timestamp, style: .date).font(.ioMono(14)).foregroundColor(.ioText)
                                    + Text(" ")
                                    + Text(snap.timestamp, style: .time).font(.ioMono(14)).foregroundColor(.ioText)
                                }
                            }

                            VStack(alignment: .leading, spacing: 6) {
                                HStack {
                                    Text("Witness Signature on File:")
                                        .font(.ioCaption(.bold))
                                        .foregroundColor(.ioTextSecondary)
                                        .textCase(.uppercase)
                                    Spacer()
                                }
                                Text(model.witnessSignature.isEmpty ? "Not yet signed" : model.witnessSignature)
                                    .font(.ioMono(14))
                                    .foregroundColor(.ioTextSecondary)
                                    .lineLimit(nil)
                            }
                            .padding(12)
                            .background(Color.ioBackgroundDark)
                            .cornerRadius(8)
                            .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.ioBorderSubtle, lineWidth: 1))
                        }

                        // The one and only result — driven entirely by model.witnessResult.
                        HStack(alignment: .top, spacing: 8) {
                            Image(systemName: witnessResultDisplay.icon)
                                .foregroundColor(witnessResultDisplay.color)
                            Text(witnessResultDisplay.text)
                                .font(.ioBody())
                                .foregroundColor(witnessResultDisplay.color)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        .padding(12)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Color.ioBackgroundDark)
                        .cornerRadius(8)
                        .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.ioBorderSubtle, lineWidth: 1))
                    }
                    .padding(18)
                    .frame(maxWidth: 820, alignment: .leading)
                    .background(Color.ioWitnessRed.opacity(0.05))
                    .background(Color.ioSurfaceDark)
                    .cornerRadius(12)
                    .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioWitnessRed.opacity(0.15), lineWidth: 1))
                }
    
                Spacer(minLength: 24)
    
                // Bottom control
                HStack {
                    Spacer()
                    VStack(alignment: .trailing, spacing: 6) {
                        Button(action: {
                            model.witnessFree()
                        }) {
                            Text("Free the Witness")
                        }
                        .buttonStyle(IoOutlineButtonStyle())
                        
                        Text("Free the witness to reset your DNS cache.")
                            .font(.ioCaption())
                            .foregroundColor(.ioTextSecondary)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .topLeading)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        // M-01 (Linux RG-2 adoption): replacing the signing key is irreversible and silently
        // invalidates every testimony already sealed with the old one. It now requires an explicit
        // confirmation; until this returns, NOTHING has been written to the Keychain.
        .alert("Replace the signing key on this machine?",
               isPresented: $model.witnessKeyOverwritePending) {
            Button("Cancel", role: .cancel) { pendingKeyWrite = nil }
            Button("Replace key", role: .destructive) { confirmKeyOverwrite() }
        } message: {
            Text("A signing key is already stored here. Replacing it destroys the old key: every testimony you have already sealed will stop verifying, and it cannot be recovered. Nothing has been changed yet.")
        }
    }

    /// M-01: resume the interrupted action, once, with the user's explicit consent. A refusal or a
    /// failed write still stops the follow-on action — consent to replace the key is not consent to
    /// seal or verify with a key that was never stored.
    private func confirmKeyOverwrite() {
        let action = pendingKeyWrite
        pendingKeyWrite = nil
        switch action {
        case .testify:
            guard model.saveWitnessKey(witnessKeyInput, confirmOverwrite: true) else { return }
            witnessKeyInput = ""
            model.witnessTestify()
        case .verify:
            guard model.saveWitnessKey(witnessKeyInput, confirmOverwrite: true) else { return }
            witnessKeyInput = ""
            model.witnessVerify()
        case .generate:
            guard model.generateWitnessKey(confirmOverwrite: true) else { return }
            witnessKeyInput = ""
        case .none:
            break
        }
    }
}

#if DEBUG
#Preview {
    WitnessView()
        .environmentObject(SyxxPreviewSupport.appModel(selection: .witness, pro: true))
        .syxxPreviewCanvas()
}
#endif
