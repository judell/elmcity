import sys
sys.path.append("c:\\users\\jon\\aptc")
sys.path.append("c:\\users\\jon\\aptc")
sys.path.append("c:\\program files\\ironpython 2.6\\lib")
import clr

clr.AddReference("CalendarAggregator")
import CalendarAggregator
from CalendarAggregator import *

try:
  minutes = sys.argv[1]
except:
  minutes = 30

print Utils.GetRecentLogEntries(int(minutes), "")
