import Foundation
import SyxxKit

// MARK: - Local Unbound resolver (Dashboard "Enable Unbound DNS")

extension AppModel {

    /// Enable/disable the local validating resolver as the system DNS. Honest tri-state; never flips
    /// `unboundEnabled` true unless the manager actually reports `.enabled`.
    @MainActor func setUnbound(_ on: Bool) {
        guard !unboundBusy else { return }
        unboundBusy = true
        Task {
            let result: UnboundResolver.UnboundResult
            if on {
                result = await unboundManager.enable()
            } else {
                result = unboundManager.disable()
            }
            await MainActor.run {
                self.unboundState = result
                switch result {
                case .enabled:
                    self.unboundEnabled = true
                    self.unboundStatusMessage = "Local Unbound resolver active."
                case .disabled:
                    self.unboundEnabled = false
                    self.unboundStatusMessage = "Local Unbound resolver disabled; original DNS restored."
                case .notInstalled(let hint):
                    self.unboundEnabled = false
                    self.unboundStatusMessage = hint
                case .elevationRequired(let step):
                    // D088: the GUI cannot grant elevation in-process (the XPC helper is not
                    // yet wired into UnboundManager). State the truth: point the user at the
                    // CLI workaround rather than presenting a toggle that can only ever fail.
                    self.unboundEnabled = false
                    self.unboundStatusMessage = "Administrator rights required: \(step). To enable from Terminal, run: sudo syxx unbound enable"
                case .failed(let reason):
                    self.unboundEnabled = false
                    self.unboundStatusMessage = "Could not enable: \(reason)"
                case .inconclusive(let reason):
                    self.unboundEnabled = false
                    self.unboundStatusMessage = "Unconfirmed: \(reason)"
                }
                self.unboundBusy = false
            }
        }
    }
}
