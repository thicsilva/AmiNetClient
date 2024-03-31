using System.Text;
using AnAmiClient;
using Microsoft.AspNetCore.SignalR;

namespace AmiNetWebTest;

public class Listener
{
    private AmiNetClient _client;
    private readonly IHubContext<AsteriskHub> _hub;

    public Listener(IHubContext<AsteriskHub> hub)
    {
        _hub = hub;
    }

    public async Task Start()
    {
        if (_client is { IsConnected: true })
            return;
        _client = new AmiNetClient("192.168.15.3", 5038);
        _client.DataSent += (_, args) =>
        {
            Console.WriteLine(Encoding.UTF8.GetString(args.Data));
        };
        _client.DataReceived += (_, args) =>
        {
            Console.WriteLine(Encoding.UTF8.GetString(args.Data));
        };
        _client.StartAsync();
        if (!await _client.Login("thiago", "e83d75e142f20955f8a13304276cb778"))
            await _hub.Clients.All.SendAsync("NotAuthenticated");
        _client.AddEventListener(new AmiNetEvent {{"Event", "ExtensionStatus"}}, SendExtensionStatus);
        _client.AddEventListener(new AmiNetEvent {{"Event", "PeerStatus"}}, SendExtensionStatus);
        _client.AddEventListener(new AmiNetEvent {{"Event", "RTCPSent"}, {"Context",  "from-internal"}}, SendExtensionStatus);
    }

    public async Task Stop()
    {
        if (_client.IsConnected)
            await _client.Logoff();
        _client.Stop();
    }

    private async Task SendExtensionStatus(AmiNetMessage message)
    {
        await _hub.Clients.All.SendAsync("ExtensionStatus", message);
    }


    public async Task ShowExtensions()
    {
        AmiNetMessage action = await _client.Publish(new AmiNetMessage() { { "Action", "SIPpeers" } });
        List<Extension> extensions = new();
        if (action.IsSuccess) 
            extensions.AddRange(action.Responses.Where(e => e.Fields.Any(kvp => kvp.Key.Equals("Event") && !kvp.Value.Equals("PeerlistComplete"))).Select(amiNetMessage => amiNetMessage.ToObject<Extension>()));

        await _hub.Clients.All.SendAsync("PeerInfo", extensions);
    }
}