import sys, clr

sys.path.append("c:\\users\\jon\\aptc")
clr.AddReference("System")
clr.AddReference("mscorlib")
import System

clr.AddReference("CalendarAggregator")
import CalendarAggregator
from CalendarAggregator import *

clr.AddReference("ElmcityUtils")
import ElmcityUtils 
from ElmcityUtils import *

counters = Counters.GetCounters()
snapshot = Counters.MakeSnapshot(counters)
threads = snapshot["ThreadCount"]
if ( threads > 50 ):
  TwitterApi.SendTwitterDirectMessage("judell", "threads: %s" % threads)
