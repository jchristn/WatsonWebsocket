@echo off
cd src\Test.Server\bin\debug\net6.0
start Test.Server.exe
TIMEOUT 1 > NUL
cd ..\..\..\..\..

cd src\Test.Client\bin\debug\net6.0
start Test.Client.exe
cd ..\..\..\..\..
echo on
