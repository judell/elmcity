import sys, clr
sys.path.append("c:\\users\\jon\\aptc")
import clr
import System
from System.Collections import *
from System.Text import *
clr.AddReference("ElmcityUtils")
from Elmcity import *


bs = BlobStorage.MakeDefaultBlobStorage()
print bs
data = """ottawa,on	865000
liverpool,uk	435000
guelph,on	126000
saskatoon,sk	220000
toronto,on	2480000
barcelona,spain	1580000
perkasie,pa	8636
west bountiful,ut	5337
"""
print data
r = bs.PutBlob("admin","pop.txt",Hashtable(),Encoding.UTF8.GetBytes(data),"text/plain")
print r.HttpResponse.status