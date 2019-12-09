![alt tag](https://github.com/jchristn/watsonwebsocket/blob/master/assets/watson.ico)

# Watson Websocket

[![][nuget-img]][nuget]

[nuget]:     https://www.nuget.org/packages/WatsonWebsocket/
[nuget-img]: https://badge.fury.io/nu/Object.svg

A simple C# async websocket server and client for reliable transmission and receipt of data, targeting both .NET Core and .NET Framework. 

## Test App

A test project for both client (```TestClient```) and server (```TestServer```) are included which will help you understand and exercise the class library.

## SSL

SSL is supported in WatsonWebsocket.  The constructors for ```WatsonWsServer``` and ```WatsonWsClient``` accept a ```bool``` indicating whether or not SSL should be enabled.  Since websockets, and as a byproduct WatsonWebsocket, use HTTPS, they rely on certificates within the certificate store of your operating system.  A test certificate is provided in both the ```TestClient``` and ```TestServer``` projects which can be used for testing purposes.  These should NOT be used in production.

For more information on using SSL certificates, please refer to the wiki.

## New in v2.0.2

- Fixed connection bug (thank you @wirmachenbunt)

## Server Example
```
using WatsonWebsocket;

WatsonWsServer server = new WatsonWsServer("[ip]", port, true|false);
server.ClientConnected = ClientConnected;
server.ClientDisconnected = ClientDisconnected;
server.MessageReceived = MessageReceived; 
server.Start();

static async Task<bool> ClientConnected(string ipPort, HttpListenerRequest request) 
{
    Console.WriteLine("Client connected: " + ipPort);
    return true;
}

static async Task ClientDisconnected(string ipPort) 
{
    Console.WriteLine("Client disconnected: " + ipPort);
}

static async Task MessageReceived(string ipPort, byte[] data) 
{ 
    Console.WriteLine("Message received from " + ipPort + ": " + Encoding.UTF8.GetString(data));
}
```

## Client Example
```
using WatsonWebsocket;

WatsonWsClient client = new WatsonWsClient("[server ip]", [server port], true|false);
client.ServerConnected = ServerConnected;
client.ServerDisconnected = ServerDisconnected;
client.MessageReceived = MessageReceived; 
client.Start(); 

static async Task MessageReceived(byte[] data) 
{
    Console.WriteLine("Message from server: " + Encoding.UTF8.GetString(data));
}

static async Task ServerConnected() 
{
    Console.WriteLine("Server connected");
}

static async Task ServerDisconnected() 
{
    Console.WriteLine("Server disconnected");
}
```

## Version History

Please refer to CHANGELOG.md for details.
