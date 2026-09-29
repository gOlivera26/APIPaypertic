namespace PagoTicAPI.Application.Utilities;

public static class PayPerTicIdentificationMapper
{
    public static AutomaticDebitIdentification Map(string? cuil, string? documentNumber)
    {
        var cuilDigits = DigitsOnly(cuil);
        if (cuilDigits.Length == 11)
        {
            return new AutomaticDebitIdentification("CUIT_ARG", cuilDigits, "ARG");
        }

        var documentDigits = DigitsOnly(documentNumber);
        if (documentDigits.Length == 0)
        {
            throw new InvalidOperationException("A numeric taxpayer identification is required.");
        }

        return new AutomaticDebitIdentification("DNI_ARG", documentDigits, "ARG");
    }

    private static string DigitsOnly(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsDigit).ToArray());
}
