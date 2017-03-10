@echo off
cd TestServer\bin\debug
start TestServer.exe
TIMEOUT 1 > NULL
cd ..\..\..

cd TestClient\bin\debug
start TestClient.exe
cd ..\..\..
echo on
