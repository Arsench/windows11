namespace Zenith.Platform.Windows.Files;

/// <summary>
/// Ejecuta trabajo en un hilo STA. Las APIs del shell de Windows (papelera de
/// reciclaje) esperan un apartamento STA; llamarlas desde el pool de hilos
/// funciona a veces y falla de formas raras el resto.
/// </summary>
internal static class StaExecutor
{
    public static Task<T> RunAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Zenith.ShellOperations"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }
}
