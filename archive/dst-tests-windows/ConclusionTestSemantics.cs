using Syxx.Kit;

namespace Syxx.Tests
{
    // P2a test-compile recovery (MATRIX-MIGRATION-PLAN.md §3): the Kit's Conclusion reshape split
    // the old single `Verdict` (which overloaded Hypothesis.Inconclusive as a pseudo-hypothesis)
    // into `Diagnosis` + `Conclusivity`. These helpers encode the OLD assertions' exact semantics
    // on the new shape so no guard is weakened by the rename:
    //
    //  * old `Verdict == Hypothesis.Clean`         ⇔ ReadsClean()  — the engine COMMITTED to clean
    //    (Diagnosis Clean at Probable/Conclusive). An inconclusive run whose leading hypothesis
    //    happens to be Clean is NOT a clean read.
    //  * old `Verdict == Hypothesis.Inconclusive`  ⇔ IsInconclusive() — the engine did not commit
    //    (Conclusivity Inconclusive or Untested).
    //  * old `Verdict == Hypothesis.<threat>`      ⇔ Names(threat) — the engine committed to that
    //    named threat.
    //
    // NOTE: this file guards the retired DST engine that still SHIPS on Windows (Phase 3 ports the
    // matrix engine); it must not outlive the Phase 3.3 cutover.
    internal static class ConclusionTestSemantics
    {
        public static bool ReadsClean(this Conclusion c) =>
            c.Diagnosis == Hypothesis.Clean &&
            c.Conclusivity is Conclusivity.Probable or Conclusivity.Conclusive;

        public static bool IsInconclusive(this Conclusion c) =>
            c.Conclusivity is Conclusivity.Inconclusive or Conclusivity.Untested;

        public static bool Names(this Conclusion c, Hypothesis h) =>
            c.Diagnosis == h &&
            c.Conclusivity is Conclusivity.Probable or Conclusivity.Conclusive;
    }
}
