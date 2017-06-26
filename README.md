# Watson Websocket

[![][nuget-img]][nuget]

[nuget]:     https://www.nuget.org/packages/WatsonWebsocket/
[nuget-img]: https://badge.fury.io/nu/Object.svg

A simple C# async websocket server and client with integrated framing for reliable transmission and receipt of data.  

## Test App
A test project for both client and server are included which will help you understand and exercise the class library.

## SSL
Two classes for each server and client are supplied, one without SSL support and one with.  An example certificate can be found in the TestSslClient and TestSslServer projects, which has a password of 'password'.  Refer to 'test-certificate.txt' in the TestSslServer project for instructions.  Use of SSL requires installation of a certificate and binding to a port using the certificate thumbprint.

## New in v1.1.0
- threading fixes, code cleanup, client connected signature change (thank you @BryanCrotaz!)

## Running under Mono
Watson works well in Mono environments to the extent that we have tested it. It is recommended that when running under Mono, you execute the containing EXE using --server and after using the Mono Ahead-of-Time Compiler (AOT).

NOTE: To bind to all interfaces specify '*' as an IP address representing any interface. Using '0.0.0.0' instead only works on Windows.

```
mono --aot=nrgctx-trampolines=8096,nimt-trampolines=8096,ntrampolines=4048 --server myapp.exe
mono --server myapp.exe
```

## Version History
- v1.0.x - initial release, bugfixes
