using SanApp.Domain.Events;

namespace SanApp.Domain.Tests.Events;

public sealed class VeranstaltungTests
{
    private static readonly DateTimeOffset Start = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ende = Start.AddHours(2);

    [Fact]
    public void CreateTrimsRequiredTextAndGeneratesIdentityAndVersion()
    {
        var eventItem = Veranstaltung.Create("  Sanitätsdienst  ", Start, Ende, "  Berlin  ", "  Auftraggeber  ");

        Assert.NotEqual(Guid.Empty, eventItem.Id);
        Assert.False(string.IsNullOrWhiteSpace(eventItem.Version));
        Assert.Equal("Sanitätsdienst", eventItem.Name);
        Assert.Equal("Berlin", eventItem.Ort);
        Assert.Equal("Auftraggeber", eventItem.Auftraggeber);
        Assert.Equal(VeranstaltungStatus.InPlanung, eventItem.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsMissingOrWhitespaceRequiredText(string? value)
    {
        Assert.Throws<VeranstaltungRegelException>(
            () => Veranstaltung.Create(value!, Start, Ende, "Berlin", "Auftraggeber"));
        Assert.Throws<VeranstaltungRegelException>(
            () => Veranstaltung.Create("Dienst", Start, Ende, value!, "Auftraggeber"));
        Assert.Throws<VeranstaltungRegelException>(
            () => Veranstaltung.Create("Dienst", Start, Ende, "Berlin", value!));
    }

    [Fact]
    public void CreateRejectsEndThatIsNotAfterStartByInstant()
    {
        var laterLocalTimeWithEarlierInstant = Start.AddHours(3).ToOffset(TimeSpan.FromHours(-2));

        Assert.Throws<VeranstaltungRegelException>(
            () => Veranstaltung.Create("Dienst", Start, Start, "Berlin", "Auftraggeber"));
        Assert.Throws<VeranstaltungRegelException>(
            () => Veranstaltung.Create("Dienst", laterLocalTimeWithEarlierInstant, Ende, "Berlin", "Auftraggeber"));
    }

    [Theory]
    [InlineData(VeranstaltungStatus.Geplant)]
    [InlineData(VeranstaltungStatus.Aktiv)]
    [InlineData(VeranstaltungStatus.Abgeschlossen)]
    [InlineData(VeranstaltungStatus.Abgesagt)]
    public void CreateAllowsOnlyInPlanungAsInitialStatus(VeranstaltungStatus initialStatus)
    {
        Assert.Throws<VeranstaltungRegelException>(
            () => Veranstaltung.Create("Dienst", Start, Ende, "Berlin", "Auftraggeber", initialStatus));
    }

    [Fact]
    public void UpdateIsAllowedThroughGeplantAndRotatesVersion()
    {
        var eventItem = Veranstaltung.Create("Dienst", Start, Ende, "Berlin", "Auftraggeber");
        var originalVersion = eventItem.Version;
        eventItem.AendereDaten("  Neuer Dienst ", Start.AddDays(1), Ende.AddDays(1), " Bonn ", " Auftraggeber  ", originalVersion);

        Assert.Equal("Neuer Dienst", eventItem.Name);
        Assert.Equal("Bonn", eventItem.Ort);
        Assert.NotEqual(originalVersion, eventItem.Version);

        eventItem.AendereStatus(VeranstaltungStatus.Geplant, eventItem.Version);
        var plannedVersion = eventItem.Version;
        eventItem.AendereDaten("Aktualisiert", Start.AddDays(2), Ende.AddDays(2), "Köln", "Auftraggeber", plannedVersion);
        Assert.Equal("Aktualisiert", eventItem.Name);
    }

    [Theory]
    [InlineData(VeranstaltungStatus.Aktiv)]
    [InlineData(VeranstaltungStatus.Abgeschlossen)]
    [InlineData(VeranstaltungStatus.Abgesagt)]
    public void UpdateAfterPlanningIsRejectedWithoutChangingState(VeranstaltungStatus status)
    {
        var eventItem = Veranstaltung.Reconstitute(Guid.NewGuid(), "Dienst", Start, Ende, "Berlin", "Auftraggeber", status, "old-version");
        var version = eventItem.Version;

        Assert.Throws<VeranstaltungRegelException>(
            () => eventItem.AendereDaten("Neuer Name", Start, Ende, "Bonn", "Auftraggeber", version));

        Assert.Equal("Dienst", eventItem.Name);
        Assert.Equal("Berlin", eventItem.Ort);
        Assert.Equal(version, eventItem.Version);
    }

    [Fact]
    public void StaleUpdateIsRejectedWithoutChangingState()
    {
        var eventItem = Veranstaltung.Create("Dienst", Start, Ende, "Berlin", "Auftraggeber");

        Assert.Throws<VeranstaltungRegelException>(
            () => eventItem.AendereDaten("Neuer Name", Start, Ende, "Bonn", "Auftraggeber", "stale"));

        Assert.Equal("Dienst", eventItem.Name);
        Assert.Equal("Berlin", eventItem.Ort);
    }

    [Theory]
    [InlineData(VeranstaltungStatus.InPlanung, true)]
    [InlineData(VeranstaltungStatus.Geplant, false)]
    [InlineData(VeranstaltungStatus.Aktiv, false)]
    [InlineData(VeranstaltungStatus.Abgeschlossen, false)]
    [InlineData(VeranstaltungStatus.Abgesagt, false)]
    public void DeletionIsAllowedOnlyInPlanung(VeranstaltungStatus status, bool allowed)
    {
        var eventItem = Veranstaltung.Reconstitute(Guid.NewGuid(), "Dienst", Start, Ende, "Berlin", "Auftraggeber", status, "version");

        if (allowed)
        {
            eventItem.PruefeLoeschbarkeit("version");
        }
        else
        {
            Assert.Throws<VeranstaltungRegelException>(() => eventItem.PruefeLoeschbarkeit("version"));
        }
    }
}
