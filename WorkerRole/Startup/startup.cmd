echo starting from %cd% >> Startup\startup.log
time /t >> Startup\startup.log
echo dir %cd% >> Startup\startup.log
dir Startup >> Startup\startup.log

echo "installing LogParser" >> Startup\startup.log
wget http://elmcity.blob.core.windows.net/admin/LogParser.exe 2>> Startup\startup.log
wget http://elmcity.blob.core.windows.net/admin/LogParser.dll 2>> Startup\startup.log

echo "installing office chart component into startup3.log" >> Startup\startup.log
wget http://elmcity.blob.core.windows.net/admin/owc10se.msi 2>> Startup\startup.log
msiexec /i owc10se.msi /qn /Lime Startup\startup3.log
time /t >> Startup\startup.log

echo running startup.py >> Startup\startup.log

ipy Startup\startup.py

echo stopping >> Startup\startup.log


