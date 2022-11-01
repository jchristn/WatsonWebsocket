$project_dir=$(pwd)

cd src/Test.Server/bin/debug/net6.0
./Test.Server
TIMEOUT 1 > NUL
cd $project_dir

cd src/Test.Client/bin/debug/net6.0
./Test.Client
cd $project_dir
