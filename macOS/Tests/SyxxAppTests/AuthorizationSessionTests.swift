import XCTest
import Security
@testable import SyxxApp

/// Regression coverage for the owner-reported password-prompt storm: "your system could not cache
/// privilege between four, sometimes as many as twelve, demands that I enter my password."
///
/// Prompt source (a) was `PrivilegeManager`'s two privileged entry points each declaring their OWN
/// local `var authRef: AuthorizationRef?`, calling `AuthorizationCreate`, and freeing it on return.
/// Nothing survived a call, so macOS had no live authorization to extend and asked again every time.
///
/// WHAT THESE TESTS CAN AND CANNOT PROVE.
///  * They CAN prove the caching contract: one factory invocation per process no matter how many
///    privileged operations run, concurrently or not; a refusal never cached; teardown re-arms.
///    One factory invocation IS one password prompt, so the count is the defect's observable.
///  * They CANNOT prove the on-screen prompt count on the owner's machine. That needs a real
///    launch of the signed app bundle and a human counting prompts, and it is still bounded by the
///    ad-hoc-signature problem documented at `SyxxKit/LicenseManager.swift:84-88`.
///
/// `testRealAuthorizationCreateIsReusedNotRecreated` is a REAL run against the live Security
/// framework: it creates a genuine `AuthorizationRef` with an EMPTY rights set, which by design
/// grants immediately and never presents UI, and proves the very same handle comes back on the
/// second call. No test here can request `kSMRightModifySystemDaemons` with
/// `.interactionAllowed` — that would put a password dialog in front of the suite.
final class AuthorizationSessionTests: XCTestCase {

    // MARK: - Real Security-framework run

    /// REAL RUN: a genuine `AuthorizationCreate` (empty rights => no prompt, no UI) is made exactly
    /// once across five acquisitions, and every acquisition hands back the IDENTICAL ref.
    ///
    /// FAILURE CASE: restore the old per-call `AuthorizationCreate` + `AuthorizationFree` shape and
    /// `creationAttempts` becomes 5 and the refs differ — which is precisely five password prompts
    /// in the shipping app.
    func testRealAuthorizationCreateIsReusedNotRecreated() {
        let session = AuthorizationSession { out in
            // Empty rights set: a real, valid AuthorizationRef with no rights extended. This is the
            // only form of AuthorizationCreate that is safe to call from a test runner.
            AuthorizationCreate(nil, nil, [], out)
        }
        defer { session.teardown() }

        var refs: [AuthorizationRef] = []
        for _ in 0..<5 {
            guard case .granted(let ref) = session.acquire() else {
                return XCTFail("live AuthorizationCreate(nil, nil, [], _) must succeed on macOS")
            }
            refs.append(ref)
        }

        XCTAssertEqual(session.creationAttempts, 1,
            "five privileged operations must cost ONE AuthorizationCreate, i.e. one password prompt")
        XCTAssertEqual(refs.count, 5)
        for ref in refs {
            XCTAssertEqual(ref, refs[0], "every acquisition must return the SAME process-wide ref")
        }
        XCTAssertTrue(session.isHoldingRights)
    }

    // MARK: - Caching contract (deterministic stub)

    func testGrantIsCachedAcrossManyAcquisitions() {
        let stub = CountingAuthorizationFactory(result: .success)
        let session = AuthorizationSession(create: stub.create, free: FreeRecorder().free)

        for _ in 0..<12 { _ = session.acquire() }

        XCTAssertEqual(session.creationAttempts, 1,
            "12 privileged operations must not produce 12 authorization prompts")
        XCTAssertEqual(stub.invocations, 1)
    }

    /// The lock is load-bearing: `PrivilegeManager` is @MainActor but `AuthorizationSession.shared`
    /// is reachable from detached Tasks (the cage capture runs on one). Without the lock, two
    /// concurrent first-uses race and BOTH prompt.
    ///
    /// FAILURE CASE: drop the `NSLock` and this asserts a count > 1 intermittently.
    func testConcurrentFirstUsesProduceExactlyOnePrompt() {
        let stub = CountingAuthorizationFactory(result: .success)
        let session = AuthorizationSession(create: stub.create, free: FreeRecorder().free)

        let group = DispatchGroup()
        for _ in 0..<32 {
            group.enter()
            DispatchQueue.global().async {
                _ = session.acquire()
                group.leave()
            }
        }
        XCTAssertEqual(group.wait(timeout: .now() + 10), .success)

        XCTAssertEqual(session.creationAttempts, 1,
            "32 concurrent privileged operations must still cost exactly one prompt")
    }

    // MARK: - Refusal is never cached, and never reads as success

    /// A cancelled prompt must surface as `.denied` and must NOT be remembered: the next privileged
    /// operation has to ask again. Caching a refusal would either be mistaken for a grant or would
    /// permanently suppress privileged work for the rest of the session.
    ///
    /// FAILURE CASE: store the failed ref (or a "we already tried" flag) and `invocations` stops
    /// rising, or the second acquisition reports `.granted`.
    func testCancelledAuthorizationIsDeniedAndNotCached() {
        let stub = CountingAuthorizationFactory(result: .failure(errAuthorizationCanceled))
        let session = AuthorizationSession(create: stub.create, free: FreeRecorder().free)

        for attempt in 1...3 {
            switch session.acquire() {
            case .denied(let status):
                XCTAssertEqual(status, errAuthorizationCanceled)
            case .granted:
                XCTFail("a cancelled authorization must never read as granted")
            case .unavailable(let status):
                XCTFail("errAuthorizationCanceled must classify as .denied, got .unavailable(\(status))")
            }
            XCTAssertEqual(stub.invocations, attempt,
                "a refusal must not be cached — attempt \(attempt) must re-ask")
            XCTAssertFalse(session.isHoldingRights,
                "no rights may be recorded after a refusal")
        }
    }

    /// "The user said no" and "we never got to ask" are different facts. An internal Security
    /// failure must classify as `.unavailable`, not as a user decision.
    ///
    /// FAILURE CASE: collapse both into one case and this fails.
    func testInternalFailureIsUnavailableNotDenied() {
        let stub = CountingAuthorizationFactory(result: .failure(errAuthorizationInternal))
        let session = AuthorizationSession(create: stub.create, free: FreeRecorder().free)

        switch session.acquire() {
        case .unavailable(let status):
            XCTAssertEqual(status, errAuthorizationInternal)
        case .denied:
            XCTFail("an internal Security failure is not a user denial")
        case .granted:
            XCTFail("a failed AuthorizationCreate must never read as granted")
        }
        XCTAssertFalse(session.isHoldingRights)
    }

    /// A factory that reports success but hands back no ref is a malformed grant, not a grant.
    /// Nothing may be cached and nothing may be handed to `SMJobBless`.
    func testSuccessStatusWithNilRefIsNotTreatedAsGranted() {
        let stub = CountingAuthorizationFactory(result: .successWithNilRef)
        let session = AuthorizationSession(create: stub.create, free: FreeRecorder().free)

        if case .granted = session.acquire() {
            XCTFail("errAuthorizationSuccess with a nil ref must never read as granted")
        }
        XCTAssertFalse(session.isHoldingRights)
        XCTAssertEqual(stub.invocations, 1)
        _ = session.acquire()
        XCTAssertEqual(stub.invocations, 2, "a malformed grant must not be cached either")
    }

    // MARK: - Teardown is the only re-prompt

    /// `.destroyRights` belongs ONLY in a deliberate teardown. After teardown the next operation
    /// legitimately re-creates (and, in the app, re-prompts).
    ///
    /// FAILURE CASE: put `.destroyRights` back on the per-call free path and the caching tests
    /// above go red; remove the cache-clear from `teardown()` and this one goes red.
    func testTeardownClearsTheCachedGrantAndDestroysRights() {
        let stub = CountingAuthorizationFactory(result: .success)
        let recorder = FreeRecorder()
        let session = AuthorizationSession(create: stub.create, free: recorder.free)

        _ = session.acquire()
        _ = session.acquire()
        XCTAssertEqual(stub.invocations, 1)
        XCTAssertTrue(session.isHoldingRights)
        XCTAssertEqual(recorder.calls.count, 0,
            "a privileged operation must NEVER free the shared ref — that is the whole defect")

        session.teardown()
        XCTAssertFalse(session.isHoldingRights)
        XCTAssertEqual(recorder.calls.count, 1)
        XCTAssertEqual(recorder.calls.first?.flags, [.destroyRights],
            "teardown, and only teardown, may destroy the acquired rights")

        _ = session.acquire()
        XCTAssertEqual(stub.invocations, 2, "after teardown the next operation must acquire afresh")
    }

    // MARK: - Test double

    /// Counts factory invocations. One invocation is one password prompt in the shipping app, so
    /// this counter is the whole point of the fix.
    ///
    /// The refs it vends are NOT real `AuthorizationRef`s and are never passed to the Security
    /// framework; they only have to be distinct, non-null pointers so identity comparisons mean
    /// something. Sessions built on this double must also be given a `FreeRecorder`, because
    /// calling the real `AuthorizationFree` on a fabricated pointer segfaults the test runner
    /// (observed, 2026-07-29).
    private final class CountingAuthorizationFactory: @unchecked Sendable {
        enum Result {
            case success
            case successWithNilRef
            case failure(OSStatus)
        }

        private let lock = NSLock()
        private let result: Result
        private var count = 0

        init(result: Result) { self.result = result }

        var invocations: Int {
            lock.lock(); defer { lock.unlock() }
            return count
        }

        func create(_ out: UnsafeMutablePointer<AuthorizationRef?>) -> OSStatus {
            lock.lock()
            count += 1
            let n = count
            lock.unlock()

            switch result {
            case .success:
                // Distinct, non-null sentinel pointers so "same ref returned twice" is meaningful.
                out.pointee = AuthorizationRef(bitPattern: 0x1000 + n)
                return errAuthorizationSuccess
            case .successWithNilRef:
                out.pointee = nil
                return errAuthorizationSuccess
            case .failure(let status):
                out.pointee = nil
                return status
            }
        }
    }

    /// Records `AuthorizationFree` calls instead of making them. Two jobs: it keeps fabricated
    /// pointers away from the Security framework, and it makes "was the shared ref freed, and with
    /// which flags?" directly assertable.
    private final class FreeRecorder: @unchecked Sendable {
        struct Call { let ref: AuthorizationRef; let flags: AuthorizationFlags }

        private let lock = NSLock()
        private var recorded: [Call] = []

        var calls: [Call] {
            lock.lock(); defer { lock.unlock() }
            return recorded
        }

        func free(_ ref: AuthorizationRef, _ flags: AuthorizationFlags) -> OSStatus {
            lock.lock()
            recorded.append(Call(ref: ref, flags: flags))
            lock.unlock()
            return errAuthorizationSuccess
        }
    }
}
