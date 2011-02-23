import clr, sys

clr.AddReference("System")
clr.AddReference("mscorlib")
import System

clr.AddReference("CalendarAggregator")
import CalendarAggregator

clr.AddReference("ElmcityUtils")
from ElmcityUtils import *

GenUtils.LogMsg("info", "_run.py starting", None)

#include common.py

lib_dir = get_local_storage() + '/Lib'
sys.path.append(lib_dir)

import traceback

"""
try:
  args = System.Collections.Generic.List[str]()
  args.Add('')
  args.Add('')
  args.Add('')
  script_url = CalendarAggregator.Configurator.monitor_script_url
  PythonUtils.RunIronPython(lib_dir, script_url, args)
except:
  GenUtils.LogMsg('info', 'monitor.py', format_traceback() )  
"""

result = ''

GenUtils.LogMsg("info", "_run.py stopping", None)
