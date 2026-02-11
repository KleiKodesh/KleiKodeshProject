@echo off
echo Copying Vue build files to C# Release output...
xcopy /E /Y "zayit-vue\dist\*" "Zayit-cs\ZayitWrapper\bin\Release\zayit-vue-app\"
echo Done!
pause
