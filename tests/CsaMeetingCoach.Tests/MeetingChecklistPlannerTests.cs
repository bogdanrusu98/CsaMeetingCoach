using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class MeetingChecklistPlannerTests
{
    [Fact]
    public void CreateChecklist_ForVbd_IncludesPurposeAndBusinessValue()
    {
        var planner = new MeetingChecklistPlanner();
        var purpose = TestData.CreatePurpose();

        var checklist = planner.CreateChecklist(purpose, requestedChecklist: null);

        Assert.Contains(
            checklist,
            item => item.Title.Contains("customer objective", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            checklist,
            item => item.Title.Contains("business value", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            checklist.SelectMany(item => item.EvidenceHints),
            hint => hint.Contains("modernization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateChecklist_WithCustomItems_PreservesRequestedScope()
    {
        var planner = new MeetingChecklistPlanner();
        var custom = new[]
        {
            new ChecklistSeed(
                "Confirm data residency",
                "The customer confirms the required region.",
                ["Romania", "EU Data Boundary"])
        };

        var checklist = planner.CreateChecklist(TestData.CreatePurpose(), custom);

        var item = Assert.Single(checklist);
        Assert.Equal("Confirm data residency", item.Title);
        Assert.Equal(ChecklistItemStatus.Pending, item.Status);
    }

    [Fact]
    public void CreateChecklist_WithMalformedCustomItem_RejectsRequest()
    {
        var planner = new MeetingChecklistPlanner();
        var malformed = new[]
        {
            new ChecklistSeed(
                "Confirm data residency",
                null!,
                ["Romania"])
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            planner.CreateChecklist(TestData.CreatePurpose(), malformed));

        Assert.Contains("completion criteria", exception.Message);
    }
}
