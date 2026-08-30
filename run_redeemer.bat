@echo off
setlocal
python "%~dp0redeem_codes.py" --config "%~dp0config.json" %*
endlocal
