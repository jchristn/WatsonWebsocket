@echo off
cd TestServer\bin\debug\net452
start TestServer.exe
TIMEOUT 1 > NULL
cd ..\..\..\..

cd TestClient\bin\debug\net452
start TestClient.exe
cd ..\..\..\..
echo on
