import clr, sys
clr.AddReference('System')
import System
from System.Security.AccessControl import *

clr.AddReference('Microsoft.WindowsAzure.ServiceRuntime')
from Microsoft.WindowsAzure.ServiceRuntime import RoleEnvironment

clr.AddReference('Microsoft.Web.Administration')
import Microsoft.Web.Administration
from Microsoft.Web.Administration import *

clr.AddReference('ElmcityUtils')
import ElmcityUtils

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

log.write('owner: %s\n' % get_process_owner())
  
python_lib = local_storage + '/Lib'
log.write('python lib: %s\n' % python_lib)  
  
uri = System.Uri('http://elmcity.blob.core.windows.net/admin/python_library.zip')
ElmcityUtils.FileUtils.UnzipFromUrlToDirectory(uri, local_storage)  

sys.path.append(python_lib)
import traceback

mgr = ServerManager()
site = mgr.Sites[0]

try:
      
  def set_config_attr(element, attr_name, attr_value):
    log.write('%s/@%s was %s\n' % ( element.ElementTagName, attr_name, element.GetAttributeValue(attr_name) ) )
    element.SetAttributeValue(attr_name, attr_value)
    log.write('%s/@%s is %s\n' % ( element.ElementTagName, attr_name, element.GetAttributeValue(attr_name) ) )  

  inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
  propagation = PropagationFlags.None

  log.write('...changing permissions on local_storage...\n')
  set_permissions(local_storage, 'Everyone',        FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.ListDirectory, inheritance, propagation, AccessControlType.Allow)
  set_permissions(local_storage, 'SYSTEM',          FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow)
  set_permissions(local_storage, 'NETWORK SERVICE', FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow)
  set_permissions(local_storage, 'Administrators',  FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow)
  
  log.write('...changing permissions on diagnostic storage..\n')
  set_permissions(get_diagnostic_store(), 'Everyone',        FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.ListDirectory, inheritance, propagation, AccessControlType.Allow)  
  set_permissions(get_diagnostic_store(), 'SYSTEM',          FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow)
  set_permissions(get_diagnostic_store(), 'NETWORK SERVICE', FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow)  
  set_permissions(get_diagnostic_store(), 'Administrators',  FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow)      
   
  """ disabled for now, not necessary
  log.write('...sending failed requests to local storage...\n')

  try:
    log.write('from: %s\n' % site.TraceFailedRequestsLogging.Attributes['Directory'].Value)
    site.TraceFailedRequestsLogging.SetAttributeValue('Directory', local_storage)
    mgr.CommitChanges()
    log.write('to: %s\n' % site.TraceFailedRequestsLogging.Attributes['Directory'].Value)
  except:
    log.write('redirect failed requests: %s\n' % format_traceback() )
  """

  """ disabled for now, doing with appcmd instead
  
  log.write('...unlocking config section ...\n')

  try:
    cfg = mgr.GetApplicationHostConfiguration()
    section = cfg.RootSectionGroup.SectionGroups['system.webServer'].SectionGroups['security'].Sections.Item['dynamicipsecurity']
    section.OverrideModeDefault = 'Allow'
    mgr.CommitChanges()
  except:
    log.write('unlock config: %s\n' % format_traceback() )

  log.write('...configuring dynamic ip restrictions...\n')

  try:
    cfg = mgr.GetWebConfiguration(site.Name)

    section = cfg.GetSection('system.webServer/security/dynamicIpSecurity')
    set_config_attr(section, 'denyAction', 'Forbidden')

    deny_by_concurrent = section.GetChildElement('denyByConcurrentRequests')
    set_config_attr(deny_by_concurrent, 'enabled', True)
    set_config_attr(deny_by_concurrent, 'maxConcurrentRequests', 5)    

    deny_by_rate = section.GetChildElement('denyByRequestRate')
    set_config_attr(deny_by_rate, 'enabled', True);
    set_config_attr(deny_by_rate, 'maxRequests', 5)

    mgr.CommitChanges()
   
  except:
    log.write('configuring dynamic ip restrictions: %s\n' % format_traceback() )
    
"""
    
  log.write('...send a request to the webserver to get it going ...\n')

  try:
    binding = site.Bindings[0]
    bi = binding.GetAttributeValue('bindingInformation')
    import urllib2
    url = 'http://' + bi.rstrip(':')
    log.write('localhost is %s\n' % url)
    urllib2.urlopen(url).read()
  except urllib2.HTTPError:
    pass # the request will fail with 403 but that's ok, it triggers application_start
         # in the server which would otherwise not happen until a user request comes in
  except:
    log.write(format_traceback())  

  log.write('...stopping at UTC %s...\n' % System.DateTime.UtcNow.ToString())

except:
  log.write('traceback %s' % format_traceback())
  
log.close()

