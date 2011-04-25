import clr, sys
clr.AddReference('System')
import System
from System.Security.AccessControl import *

clr.AddReference('ElmcityUtils')
from ElmcityUtils import *

clr.AddReference('System.Management')
import System.Management

#region common

def get_resource_dirs():
  return System.IO.Directory.GetDirectories('c:\Resources\Directory')

def get_local_storage():
  return [dir for dir in get_resource_dirs() if dir.endswith('LocalStorage1')][0]
 
def get_diagnostic_store():
  return [dir for dir in get_resource_dirs() if dir.endswith('DiagnosticStore')][0] 

def get_log_storage():
  return [d for d in System.IO.Directory.GetDirectories("%s/LogFiles/Web" % get_diagnostic_store())][0]  

def show_rules(ds,log):
  rules = ds.GetAccessRules(True, True, System.Type.GetType('System.Security.Principal.NTAccount'))
  log.write('%s rules\n' % rules.Count)
  for rule in rules:
    log.write('%s %s %s\n' % (rule.IdentityReference.Value, rule.AccessControlType, rule.FileSystemRights))  

def set_permissions(directory, who, rights, inheritance, propagation, access_type):
  try:
    di = System.IO.DirectoryInfo(directory)
    ds = di.GetAccessControl()
    show_rules(ds, log)
    rule = FileSystemAccessRule(who, rights, inheritance, propagation, access_type)
    ds.AddAccessRule(rule)
    di.SetAccessControl(ds)
    show_rules(ds,log)
  except:
    log.write('cannot set permissions on %s' % directory )     

def get_process_owner():
  mos = System.Management.ManagementObjectSearcher("SELECT * from Win32_Process")
  for mo in mos.Get():
    oi = System.Array.CreateInstance(str,2)
    mo.InvokeMethod('GetOwner', oi)
    name = mo.GetPropertyValue('Name')
    if ( name == System.Diagnostics.Process.GetCurrentProcess().MainModule.ModuleName ):
      return [ oi[0], oi[1] ]

def format_traceback():    
  exc_type, exc_value, exc_traceback = sys.exc_info()
  tb = traceback.format_exception(exc_type, exc_value, exc_traceback)  
  return repr(tb) 
 
#endregion  

local_storage = get_local_storage()
      
log = open('Startup\startup.py.log', 'a')
log.write('...starting at UTC %s...\n' % System.DateTime.UtcNow.ToString())  

log.write('owner: %s\n' % get_process_owner() )

log.write('...installing python standard library...\n')

uri = System.Uri('http://elmcity.blob.core.windows.net/admin/python_library.zip')
FileUtils.UnzipFromUrlToDirectory(uri, get_local_storage() )

sys.path.append(get_local_storage() + '/Lib')
import traceback

try:
  log.write('...changing permissions on local_storage %s\n' % local_storage)
  inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit

  propagation = PropagationFlags.None
  set_permissions(get_local_storage(), 'Everyone', FileSystemRights.Write | FileSystemRights.Read, inheritance, propagation, AccessControlType.Allow)  
  set_permissions(get_local_storage(), 'SYSTEM', FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow)
  set_permissions(get_local_storage(), 'Administrators', FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow)

  log.write('...stopping at UTC %s...\n' % System.DateTime.UtcNow.ToString())

except:
  GenUtils.LogMsg('exception', 'startup.py', format_traceback() )    

log.close()

