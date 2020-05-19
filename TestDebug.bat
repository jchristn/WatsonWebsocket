@echo off
cd Test.Server\bin\debug\net452
start Test.Server.exe
TIMEOUT 1 > NUL
cd ..\..\..\..

cd Test.Client\bin\debug\net452
start Test.Client.exe
cd ..\..\..\..
echo on
