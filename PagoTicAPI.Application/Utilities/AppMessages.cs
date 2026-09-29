namespace PagoTicAPI.Application.Utilities;

public abstract class AppMessages
{
    public const string MsjSuccess = "La operación ha sido ejecutada exitosamente.";
    public const string MsjError = "Ha ocurrido un error inesperado, si el error persiste comuníquese con soporte.";
    public const string MsjNotFound = "No se han encontrado resultados.";
    public const string MsjUnauthorized = "No tienes permisos para realizar esta acción.";
    public const string MsjNotAuthenticate = "No estás autenticado.";
    public const string MsjErrorFile = "Hubo un problema al intentar subir el adjunto. inténtalo de nuevo más tarde. Si el problema persiste, ponte en contacto con el soporte técnico.";
    public const string MsjSuccessMasive = "La operación ha sido ejecutada exitosamente, aguarde unos minutos para que se procesen los datos. Recibirá un correo electrónico con el resultado de la operación.";
}
