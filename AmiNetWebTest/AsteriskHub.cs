using Microsoft.AspNetCore.SignalR;

namespace AmiNetWebTest;

public class AsteriskHub: Hub
{
    private readonly Listener _listener;

    public AsteriskHub(Listener listener)
    {
        _listener = listener;
    }

   

    public async Task ShowExtensions()
    {
        await _listener.ShowExtensions();
    }

    public Task Start()
    {
        return _listener.Start();
    }

    public Task Stop()
    {
         return _listener.Stop();
    }
}