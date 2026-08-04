import CryptoKit
import Foundation
import SyxxKit

struct BundledAgreement: Equatable {
    static let version = "syxx-eula-draft-2026-07-30"

    let document: AgreementDocument
    let text: String

    static func load(bundle: Bundle = .module) -> Result<BundledAgreement, AgreementResourceError> {
        guard let url = bundle.url(
            forResource: "Syxx-EULA",
            withExtension: "txt",
            subdirectory: "Resources/Legal"
        ) else {
            return .failure(.missing)
        }

        let data: Data
        do {
            data = try Data(contentsOf: url)
        } catch {
            return .failure(.unavailable)
        }
        guard let text = String(data: data, encoding: .utf8) else {
            return .failure(.invalidUTF8)
        }
        let digest = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
        return .success(
            BundledAgreement(
                document: AgreementDocument(version: version, sha256: digest),
                text: text
            )
        )
    }
}

enum AgreementResourceError: Error, Equatable {
    case missing
    case unavailable
    case invalidUTF8
}
