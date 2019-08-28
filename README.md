![alt tag](https://github.com/jchristn/watsonwebsocket/blob/master/assets/watson.ico)

# Watson Websocket

[![][nuget-img]][nuget]

[nuget]:     https://www.nuget.org/packages/WatsonWebsocket/
[nuget-img]: https://badge.fury.io/nu/Object.svg

A simple C# async websocket server and client for reliable transmission and receipt of data.  

As of v1.2.4, WatsonWebsocket now targets both .NET Core 2.0 and .NET Framework 4.5.2.

## Test App

A test project for both client (```TestClient```) and server (```TestServer```) are included which will help you understand and exercise the class library.

## SSL

SSL is supported in WatsonWebsocket.  The constructors for ```WatsonWsServer``` and ```WatsonWsClient``` accept a ```bool``` indicating whether or not SSL should be enabled.  Since websockets, and as a byproduct WatsonWebsocket, use HTTPS, they rely on certificates within the certificate store of your operating system.  A test certificate is provided in both the ```TestClient``` and ```TestServer``` projects which can be used for testing purposes.  These should NOT be used in production.

## New in v1.3.x

- Big thanks to @FodderMK for his time and contributions to WatsonWebsocket!
- Breaking change, ```ClientConnected``` now returns entire HttpListenerRequest
- Simplifications to test programs for both client and server
- More appropriate status codes for various scenarios including non-websocket requests and denied requests
 
## Version History

v1.2.x

- Return value from ```ClientConnected``` now acts as permit/deny for the connection - thank you @FodderMK!
- Bugfixes to client disconnect handling - thank you @FodderMK!
- Integrated pull requests from @FodderMK (thank you!) for fixes and GetAwaiter() 
- Retarget to support both .NET Core 2.0 and .NET Framework 4.5.2
- Enhancements and fixes, new constructor using Uri (thank you @BryanCrotaz!)
- Bugfixes, client kill API (thank you @BryanCrotaz!)

v1.1.x

- threading fixes, code cleanup, client connected signature change (thank you @BryanCrotaz!)
- Remove unnecessary framing

v1.0.x 

- initial release, bugfixes
