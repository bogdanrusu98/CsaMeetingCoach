using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Tests;

internal static class TestData
{
    public static MeetingPurpose CreatePurpose()
    {
        return new MeetingPurpose(
            "Contoso modernization",
            "VBD",
            "Align cloud modernization to measurable customer value.",
            [
                "Confirm priority workloads",
                "Agree measurable business outcomes",
                "Define owners and next steps"
            ]);
    }
}
