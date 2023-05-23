![alt tag](https://github.com/jchristn/watsonwebsocket/blob/master/assets/watson.ico)

# Watson Websocket

[![NuGet Version](https://img.shields.io/nuget/v/WatsonWebsocket.svg?style=flat)](https://www.nuget.org/packages/WatsonWebsocket/) [![NuGet](https://img.shields.io/nuget/dt/WatsonWebsocket.svg)](https://www.nuget.org/packages/WatsonWebsocket) 

WatsonWebsocket is the EASIEST and FASTEST way to build client and server applications that rely on messaging using websockets.  It's.  Really.  Easy.

## Thanks and Appreciation

Many thanks and much appreciation to those that take the time to make this library better!  

@BryanCrotaz @FodderMK @caozero @Danatobob @Data33 @AK5nowman @jjxtra @MartyIX @rajeshdua123 
@tersers @MacKey-255 @KRookoo1 @joreg @ilsnk @xbarra @mawkish00 @jlopvet @marco-manfroni-perugiatiming 
@GiaNTizmO @exergist @ebarale99 @WarstekHUN @Rubidium37 @codengine

## Test App

A test project for both client (```TestClient```) and server (```TestServer```) are included which will help you understand and exercise the class library.

A test project that spawns a server and client and exchanges messages can be found here: https://github.com/jchristn/watsonwebsockettest

## Supported Operating Systems

WatsonWebsocket currently relies on websocket support being present in the underlying operating system.  Windows 7 **does not** support websockets.

## SSL

SSL is supported in WatsonWebsocket.  The constructors for ```WatsonWsServer``` and ```WatsonWsClient``` accept a ```bool``` indicating whether or not SSL should be enabled.  Since websockets, and as a byproduct WatsonWebsocket, use HTTPS, they rely on certificates within the certificate store of your operating system.  A test certificate is provided in both the ```TestClient``` and ```TestServer``` projects which can be used for testing purposes.  These should NOT be used in production.

For more information on using SSL certificates, please refer to the wiki.

## New in v4.0.x

- Breaking changes
- Clients now identified by ```Guid``` in ```ClientMetadata```
- ```ListClients``` now returns full ```ClientMetadata```
- ```Send*``` methods now take ```guid``` as opposed to ```IpPort```
- Add targeting for .NET 7.0 and .NET Framework 4.8
- Fix for Blazor WASM, thank you @ebarale99
- Fix for invalid control characters, thank you @WarstekHUN

## Server Example
```csharp
using WatsonWebsocket;

WatsonWsServer server = new WatsonWsServer("[ip]", port, true|false);
server.ClientConnected += ClientConnected;
server.ClientDisconnected += ClientDisconnected;
server.MessageReceived += MessageReceived; 
server.Start();

static void ClientConnected(object sender, ConnectionEventArgs args) 
{
    Console.WriteLine("Client connected: " + args.Client.ToString());
}

static void ClientDisconnected(object sender, DisconnectionEventArgs args) 
{
    Console.WriteLine("Client disconnected: " + args.Client.ToString());
}

static void MessageReceived(object sender, MessageReceivedEventArgs args) 
{ 
    Console.WriteLine("Message received from " + args.Client.ToString() + ": " + Encoding.UTF8.GetString(args.Data));
}
```

## Client Example
```csharp
using WatsonWebsocket;

WatsonWsClient client = new WatsonWsClient("[server ip]", [server port], true|false);
client.ServerConnected += ServerConnected;
client.ServerDisconnected += ServerDisconnected;
client.MessageReceived += MessageReceived; 
client.Start(); 

static void MessageReceived(object sender, MessageReceivedEventArgs args) 
{
    Console.WriteLine("Message from server: " + Encoding.UTF8.GetString(args.Data));
}

static void ServerConnected(object sender, EventArgs args) 
{
    Console.WriteLine("Server connected");
}

static void ServerDisconnected(object sender, EventArgs args) 
{
    Console.WriteLine("Server disconnected");
}
```

## Client Example using Browser
```csharp
server = new WatsonWsServer("http://localhost:9000/");
server.Start();
```

```js
let socket = new WebSocket("ws://localhost:9000/test/");
socket.onopen = function () { console.log("success"); };
socket.onmessage = function (msg) { console.log(msg.data); };
socket.onclose = function () { console.log("closed"); };
// wait a moment
socket.send("Hello, world!");
```

## Accessing from Outside Localhost

When you configure WatsonWebsocket to listen on ```127.0.0.1``` or ```localhost```, it will only respond to requests received from within the local machine.

To configure access from other nodes outside of ```localhost```, use the following:

- Specify the exact DNS hostname upon which WatsonWebsocket should listen in the Server constructor. The HOST header on incoming HTTP requests MUST match this value (this is an operating system limitation)
- If you want to listen on more than one hostname or IP address, use ```*``` or ```+```. You MUST:
  - Run WatsonWebsocket as administrator for this to work (this is an operating system limitation)
  - Use the server constructor that takes distinct hostname and port values (not the URI-based constructor)
- If you want to use a port number less than 1024, you MUST run WatsonWebsocket as administrator (this is an operating system limitation)
- If you listen on an interface IP address other than ```127.0.0.1```, you MAY need to run as administrator (this is operating system dependent)
- Open a port on your firewall to permit traffic on the TCP port upon which WatsonWebsocket is listening
- You may have to add URL ACLs, i.e. URL bindings, within the operating system using the ```netsh``` command:
  - Check for existing bindings using ```netsh http show urlacl```
  - Add a binding using ```netsh http add urlacl url=http://[hostname]:[port]/ user=everyone listen=yes```
  - Where ```hostname``` and ```port``` are the values you are using in the constructor
- If you are using SSL, you will need to install the certificate in the certificate store and retrieve the thumbprint
  - Refer to https://github.com/jchristn/WatsonWebserver/wiki/Using-SSL-on-Windows for more information, or if you are using SSL
- If you're still having problems, please do not hesitate to file an issue here, and I will do my best to help and update the documentation.

## Version History

Please refer to CHANGELOG.md for details.
