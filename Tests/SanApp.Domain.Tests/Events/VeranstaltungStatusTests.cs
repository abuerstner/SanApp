using SanApp.Domain.Events;

namespace SanApp.Domain.Tests.Events;

public sealed class VeranstaltungStatusTests
{
    private static readonly DateTimeOffset Start = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ende = Start.AddHours(2);

    public static IEnumerable<object[]> Statuspaare =>
        from von in Enum.GetValues<VeranstaltungStatus>()
        from nach in Enum.GetValues<VeranstaltungStatus>()
        select new object[] { von, nach };

    [Theory]
    [MemberData(nameof(Statuspaare))]
    public void StatuswechselErlaubtGenauDieSechsFestgelegtenPaare(VeranstaltungStatus von, VeranstaltungStatus nach)
    {
        var eventItem = Veranstaltung.Reconstitute(Guid.NewGuid(), "Dienst", Start, Ende, "Berlin", "Auftraggeber", von, "version");
        var erwarteterErfolg = (von, nach) is
            (VeranstaltungStatus.InPlanung, VeranstaltungStatus.Geplant)
            or (VeranstaltungStatus.InPlanung, VeranstaltungStatus.Abgesagt)
            or (VeranstaltungStatus.Geplant, VeranstaltungStatus.Aktiv)
            or (VeranstaltungStatus.Geplant, VeranstaltungStatus.Abgesagt)
            or (VeranstaltungStatus.Aktiv, VeranstaltungStatus.Abgeschlossen)
            or (VeranstaltungStatus.Aktiv, VeranstaltungStatus.Abgesagt);

        if (erwarteterErfolg)
        {
            eventItem.AendereStatus(nach, "version");
            Assert.Equal(nach, eventItem.Status);
            Assert.NotEqual("version", eventItem.Version);
        }
        else
        {
            Assert.Throws<VeranstaltungRegelException>(() => eventItem.AendereStatus(nach, "version"));
            Assert.Equal(von, eventItem.Status);
            Assert.Equal("version", eventItem.Version);
        }
    }
}
