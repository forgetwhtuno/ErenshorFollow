using System;
using ErenshorFollow;

internal static class ExpeditionOrderProofPolicyTests
{
    private static int _passed;

    public static int Main()
    {
        Run("complete planner and mover records the same successful contract", CompleteContract);
        Run("accepted partial planner and partial mover remain distinguishable", PartialContract);
        Run("planner acceptance can coexist with a complete mover probe", PartialPlannerCompleteMover);
        Run("mover sample failure has a deterministic reason", SampleFailure);
        Run("ownership/order failures preserve correlation fields", OwnershipAndOrderFailure);
        Run("diagnostic failure formatting is non-throwing", DiagnosticFailure);
        Console.WriteLine("PASS: " + _passed + " expedition order-proof policy tests.");
        return 0;
    }

    private static ExpeditionOrderProofPolicy.Record Base()
    {
        return new ExpeditionOrderProofPolicy.Record { Session = 7, Order = 3, Scene = "Hidden", Leader = "Phanty",
            DestinationZone = "Duskenlight", CrossingKey = "duskenlight/key", CandidateId = "duskenlight/key/a2",
            SelectedSeed = "vert2", PlannerTarget = "(1, 2, 3)", PlannerSample = "(1, 2, 3)",
            PlannerCorners = 21, PlannerResult = "PartialNearCrossing", PlannerAccepted = true,
            PlannerReason = "partial route makes meaningful progress", MovementOwnershipBefore = false,
            MovementOwnershipAfter = true, MoverRawTarget = "(1, 2, 3)", MoverSample = "(1, 2, 3)",
            Mover2mSample = true, MoverCorners = 21, MoverEndpoint = "(1, 2, 3)",
            MoverEndpointDistanceToTarget = 0f, HighPriorityNavTarget = "(1, 2, 3)", FailureStage = "none", FailureReason = "none" };
    }

    private static void CompleteContract()
    {
        var r = Base(); r.PlannerPath = "Complete"; r.MoverPath = "Complete"; r.OrderAttempted = true; r.OrderIssued = true;
        string text = ExpeditionOrderProofPolicy.Format(r);
        Require(text.IndexOf("session=7", StringComparison.Ordinal) >= 0 && text.IndexOf("plannerPath=Complete", StringComparison.Ordinal) >= 0 &&
            text.IndexOf("moverPath=Complete", StringComparison.Ordinal) >= 0 && text.IndexOf("orderIssued=True", StringComparison.Ordinal) >= 0, text);
    }

    private static void PartialContract()
    {
        var r = Base(); r.PlannerPath = "Partial"; r.MoverPath = "Partial"; r.FailureReason = "mover_path_partial_complete_required";
        string text = ExpeditionOrderProofPolicy.Format(r);
        Require(text.IndexOf("plannerAccepted=True", StringComparison.Ordinal) >= 0 && text.IndexOf("moverPath=Partial", StringComparison.Ordinal) >= 0 &&
            text.IndexOf("mover_path_partial_complete_required", StringComparison.Ordinal) >= 0, text);
    }

    private static void PartialPlannerCompleteMover()
    {
        var r = Base(); r.PlannerPath = "Partial"; r.MoverPath = "Complete";
        string text = ExpeditionOrderProofPolicy.Format(r);
        Require(text.IndexOf("plannerPath=Partial", StringComparison.Ordinal) >= 0 && text.IndexOf("moverPath=Complete", StringComparison.Ordinal) >= 0, text);
    }

    private static void SampleFailure()
    {
        Require(ExpeditionOrderProofPolicy.PathName(false, false, null) == "Invalid", "unsampled mover target must be Invalid");
        Require(ExpeditionOrderProofPolicy.Failure("mover_probe", "mover_sample_2m_failed") == "mover_probe:mover_sample_2m_failed", "failure code changed");
    }

    private static void OwnershipAndOrderFailure()
    {
        var r = Base(); r.MovementOwnershipAfter = false; r.OrderAttempted = false; r.OrderIssued = false;
        r.FailureStage = "movement_ownership"; r.FailureReason = "movement_ownership_not_acquired";
        string text = ExpeditionOrderProofPolicy.Format(r);
        Require(text.IndexOf("order=3", StringComparison.Ordinal) >= 0 && text.IndexOf("movementOwnershipAfter=False", StringComparison.Ordinal) >= 0 &&
            text.IndexOf("orderAttempted=False", StringComparison.Ordinal) >= 0 && text.IndexOf("movement_ownership_not_acquired", StringComparison.Ordinal) >= 0, text);
    }

    private static void DiagnosticFailure()
    {
        Require(ExpeditionOrderProofPolicy.Format(null).IndexOf("diagnostic_error=missing_record", StringComparison.Ordinal) >= 0, "missing record must be visible");
    }

    private static void Run(string name, Action test) { test(); _passed++; Console.WriteLine("PASS: " + name); }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}
