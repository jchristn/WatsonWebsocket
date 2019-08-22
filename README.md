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

## New in v1.2.x

- Integrated pull requests from @FodderMK (thank you!) for fixes and GetAwaiter() 
- Retarget to support both .NET Core 2.0 and .NET Framework 4.5.2

## Running under Mono

Watson works well in Mono environments to the extent that we have tested it. It is recommended that when running under Mono, you execute the containing EXE using --server and after using the Mono Ahead-of-Time Compiler (AOT).

NOTE: To bind to all interfaces specify ```*``` as an IP address representing any interface. Using ```0.0.0.0``` only works on Windows.

```
mono --aot=nrgctx-trampolines=8096,nimt-trampolines=8096,ntrampolines=4048 --server myapp.exe
mono --server myapp.exe
```

## Version History

v1.2.x
- Enhancements and fixes, new constructor using Uri (thank you @BryanCrotaz!)
- Bugfixes, client kill API (thank you @BryanCrotaz!)

v1.1.x
- threading fixes, code cleanup, client connected signature change (thank you @BryanCrotaz!)
- Remove unnecessary framing

v1.0.x 
- initial release, bugfixes
