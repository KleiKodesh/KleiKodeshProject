@echo off
echo Copying Vue build files to C# Debug output...
xcopy /E /Y "zayit-vue\dist\*" "Zayit-cs\ZayitWrapper\bin\Debug\zayit-vue-app\"
echo Done!
pause
