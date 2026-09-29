namespace PagoTicAPI.Domain.Context.Configuration;

public static class AutomaticDebitDatabaseNames
{
    public static class Tables
    {
        public const string Credentials = "T_CREDENCIALES_PAYPERTIC";
        public const string Adhesions = "T_DEBITOS_AUTOMATICOS";
        public const string Operations = "T_DEBITOS_AUTOMATICOS_DET";
        public const string Events = "T_DEB_AUT_EVENTOS";
        public const string Notifications = "T_DEB_AUT_NOTIFICACIONES";

        public static IReadOnlyList<string> All { get; } =
        [Credentials, Adhesions, Operations, Events, Notifications];
    }

    public static class Sequences
    {
        public const string Credentials = "SQ_CREDENCIALES_PAYPERTIC";
        public const string Adhesions = "SQ_DEBITOS_AUTOMATICOS";
        public const string Operations = "SQ_DEBITOS_AUTOMATICOS_DET";
        public const string Events = "SQ_DEB_AUT_EVENTOS";
        public const string Notifications = "SQ_DEB_AUT_NOTIFICACIONES";

        public static IReadOnlyList<string> All { get; } =
        [Credentials, Adhesions, Operations, Events, Notifications];
    }
}

