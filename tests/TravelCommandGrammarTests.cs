using System;
using ErenshorFollow;

internal static class TravelCommandGrammarTests
{
    private static int _failed;

    private static int Main()
    {
        string[] accepted =
        {
            "Dancer lead us to Vitheo's Watch",
            "Dancer, lead the way to Vitheo's Watch",
            "Dancer lead the group to Vitheo's Watch",
            "Dancer show us the way to Vitheo's Watch",
            "Dancer guide us to Vitheo's Watch",
            "Dancer lead me to Azure",
            "Dancer take us to Azure",
            "Dancer take me to Azure"
        };
        for (int i = 0; i < accepted.Length; i++) Expect(accepted[i], true);

        string[] rejected =
        {
            "can Dancer lead us to Vitheo's Watch?",
            "do you think Dancer can show us the way?",
            "who should guide us to Vitheo's Watch?",
            "I think Dancer should lead us there"
        };
        for (int i = 0; i < rejected.Length; i++) Expect(rejected[i], false);

        string leader;
        string destination;
        bool parsed = TravelCommandGrammar.TryParseLeadRequest(
            "Dancer, guide us to Vitheo's Watch?", out leader, out destination);
        Check("extracts direct leader", parsed && leader == "Dancer");
        Check("extracts clean destination", parsed && destination == "Vitheo's Watch");

        Console.WriteLine("[ErenshorFollow Regression] SUMMARY: " +
            (_failed == 0 ? "PASS" : "FAIL") + " (failures=" + _failed + ")");
        return _failed == 0 ? 0 : 1;
    }

    private static void Expect(string message, bool expected)
    {
        string leader;
        string destination;
        bool actual = TravelCommandGrammar.TryParseLeadRequest(message, out leader, out destination);
        Check((expected ? "accepts: " : "rejects: ") + message, actual == expected);
    }

    private static void Check(string name, bool pass)
    {
        Console.WriteLine("[ErenshorFollow Regression] " + name + ": " + (pass ? "PASS" : "FAIL"));
        if (!pass) _failed++;
    }
}
