namespace SanApp.Domain.Events;

public sealed class Veranstaltung
{
    private Veranstaltung(
        Guid id,
        string name,
        DateTimeOffset startzeitpunkt,
        DateTimeOffset endzeitpunkt,
        string ort,
        string auftraggeber,
        VeranstaltungStatus status,
        string version)
    {
        Id = id;
        Name = name;
        Startzeitpunkt = startzeitpunkt;
        Endzeitpunkt = endzeitpunkt;
        Ort = ort;
        Auftraggeber = auftraggeber;
        Status = status;
        Version = version;
    }

    public Guid Id { get; }
    public string Name { get; private set; }
    public DateTimeOffset Startzeitpunkt { get; private set; }
    public DateTimeOffset Endzeitpunkt { get; private set; }
    public string Ort { get; private set; }
    public string Auftraggeber { get; private set; }
    public VeranstaltungStatus Status { get; private set; }
    public string Version { get; private set; }

    public static Veranstaltung Create(
        string name,
        DateTimeOffset startzeitpunkt,
        DateTimeOffset endzeitpunkt,
        string ort,
        string auftraggeber,
        VeranstaltungStatus initialStatus = VeranstaltungStatus.InPlanung)
    {
        if (initialStatus != VeranstaltungStatus.InPlanung)
        {
            throw new VeranstaltungRegelException(
                VeranstaltungFehlercode.UngueltigerAnfangsstatus,
                "Neue Veranstaltungen müssen mit 'In Planung' angelegt werden.");
        }

        var normalized = ValidateAndNormalize(name, startzeitpunkt, endzeitpunkt, ort, auftraggeber);
        return new Veranstaltung(
            Guid.NewGuid(),
            normalized.Name,
            startzeitpunkt,
            endzeitpunkt,
            normalized.Ort,
            normalized.Auftraggeber,
            initialStatus,
            CreateVersion());
    }

    public static Veranstaltung Reconstitute(
        Guid id,
        string name,
        DateTimeOffset startzeitpunkt,
        DateTimeOffset endzeitpunkt,
        string ort,
        string auftraggeber,
        VeranstaltungStatus status,
        string version)
    {
        if (id == Guid.Empty)
        {
            throw new VeranstaltungRegelException(VeranstaltungFehlercode.UngueltigeIdentitaet, "Die Veranstaltungskennung ist ungültig.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new VeranstaltungRegelException(VeranstaltungFehlercode.UnbekannterStatus, "Der Veranstaltungsstatus ist ungültig.");
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new VeranstaltungRegelException(VeranstaltungFehlercode.UngueltigeVersion, "Die Änderungsversion ist ungültig.");
        }

        var normalized = ValidateAndNormalize(name, startzeitpunkt, endzeitpunkt, ort, auftraggeber);
        return new Veranstaltung(
            id,
            normalized.Name,
            startzeitpunkt,
            endzeitpunkt,
            normalized.Ort,
            normalized.Auftraggeber,
            status,
            version);
    }

    public void AendereDaten(
        string name,
        DateTimeOffset startzeitpunkt,
        DateTimeOffset endzeitpunkt,
        string ort,
        string auftraggeber,
        string erwarteteVersion)
    {
        PruefeVersion(erwarteteVersion);
        if (Status is not (VeranstaltungStatus.InPlanung or VeranstaltungStatus.Geplant))
        {
            throw new VeranstaltungRegelException(
                VeranstaltungFehlercode.DatenAenderungNichtErlaubt,
                "Veranstaltungsdaten können ab dem Status 'Aktiv' nicht mehr geändert werden.");
        }

        var normalized = ValidateAndNormalize(name, startzeitpunkt, endzeitpunkt, ort, auftraggeber);
        Name = normalized.Name;
        Startzeitpunkt = startzeitpunkt;
        Endzeitpunkt = endzeitpunkt;
        Ort = normalized.Ort;
        Auftraggeber = normalized.Auftraggeber;
        Version = CreateVersion(Version);
    }

    public void AendereStatus(VeranstaltungStatus neuerStatus, string erwarteteVersion)
    {
        PruefeVersion(erwarteteVersion);
        if (!Enum.IsDefined(neuerStatus))
        {
            throw new VeranstaltungRegelException(VeranstaltungFehlercode.UnbekannterStatus, "Der Veranstaltungsstatus ist ungültig.");
        }

        var erlaubt = (Status, neuerStatus) is
            (VeranstaltungStatus.InPlanung, VeranstaltungStatus.Geplant)
            or (VeranstaltungStatus.InPlanung, VeranstaltungStatus.Abgesagt)
            or (VeranstaltungStatus.Geplant, VeranstaltungStatus.Aktiv)
            or (VeranstaltungStatus.Geplant, VeranstaltungStatus.Abgesagt)
            or (VeranstaltungStatus.Aktiv, VeranstaltungStatus.Abgeschlossen)
            or (VeranstaltungStatus.Aktiv, VeranstaltungStatus.Abgesagt);
        if (!erlaubt)
        {
            throw new VeranstaltungRegelException(
                VeranstaltungFehlercode.StatuswechselNichtErlaubt,
                "Der angeforderte Statuswechsel ist nicht zulässig.");
        }

        Status = neuerStatus;
        Version = CreateVersion(Version);
    }

    public void PruefeLoeschbarkeit(string erwarteteVersion)
    {
        PruefeVersion(erwarteteVersion);
        if (Status != VeranstaltungStatus.InPlanung)
        {
            throw new VeranstaltungRegelException(
                VeranstaltungFehlercode.LoeschungNichtErlaubt,
                "Veranstaltungen können nur im Status 'In Planung' gelöscht werden.");
        }
    }

    private void PruefeVersion(string erwarteteVersion)
    {
        if (string.IsNullOrWhiteSpace(erwarteteVersion)
            || !string.Equals(Version, erwarteteVersion, StringComparison.Ordinal))
        {
            throw new VeranstaltungRegelException(
                VeranstaltungFehlercode.VeralteteVersion,
                "Die Veranstaltung wurde zwischenzeitlich geändert. Laden Sie den aktuellen Stand erneut.");
        }
    }

    private static (string Name, string Ort, string Auftraggeber) ValidateAndNormalize(
        string name,
        DateTimeOffset startzeitpunkt,
        DateTimeOffset endzeitpunkt,
        string ort,
        string auftraggeber)
    {
        var normalizedName = NormalizeRequired(name, nameof(name));
        var normalizedOrt = NormalizeRequired(ort, nameof(ort));
        var normalizedAuftraggeber = NormalizeRequired(auftraggeber, nameof(auftraggeber));
        if (endzeitpunkt <= startzeitpunkt)
        {
            throw new VeranstaltungRegelException(
                VeranstaltungFehlercode.UngueltigerZeitraum,
                "Das Ende muss nach dem Start liegen.");
        }

        return (normalizedName, normalizedOrt, normalizedAuftraggeber);
    }

    private static string NormalizeRequired(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new VeranstaltungRegelException(
                VeranstaltungFehlercode.Pflichtfeld,
                $"Das Pflichtfeld '{field}' darf nicht leer sein.");
        }

        return value.Trim();
    }

    private static string CreateVersion(string? previousVersion = null)
    {
        string version;
        do
        {
            version = Guid.NewGuid().ToString("N");
        }
        while (string.Equals(version, previousVersion, StringComparison.Ordinal));

        return version;
    }
}

public enum VeranstaltungFehlercode
{
    Pflichtfeld,
    UngueltigerZeitraum,
    UngueltigerAnfangsstatus,
    UnbekannterStatus,
    UngueltigeIdentitaet,
    UngueltigeVersion,
    VeralteteVersion,
    DatenAenderungNichtErlaubt,
    StatuswechselNichtErlaubt,
    LoeschungNichtErlaubt
}

public sealed class VeranstaltungRegelException(VeranstaltungFehlercode code, string message) : Exception(message)
{
    public VeranstaltungFehlercode Code { get; } = code;
}
