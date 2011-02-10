import clr, sys
clr.AddReference('System')
import System
from System.Security.AccessControl import *

clr.AddReference('Microsoft.Web.Administration')
import Microsoft.Web.Administration
from Microsoft.Web.Administration import *

clr.AddReference('ElmcityUtils')
import ElmcityUtils

def show_rules(ds,log):
  rules = ds.GetAccessRules(True, True, System.Type.GetType('System.Security.Principal.NTAccount'))
  log.write('%s rules\n' % rules.Count)
  for rule in rules:
    log.write('%s %s %s\n' % (rule.IdentityReference.Value, rule.AccessControlType, rule.FileSystemRights))
    
def get_local_storage():
  for d in System.IO.Directory.EnumerateDirectories("c:\\Resources\\Directory"):
    if d.endswith('LocalStorage1'):
      print ( 'get_local_storage: %s' % d)
      return d  

log = open('Startup\startup.py.log', 'a')
log.write('...starting at UTC %s...\n' % System.DateTime.UtcNow.ToString())

local_storage = get_local_storage()
python_lib = local_storage + '/Lib'
log.write('python lib: %s\n' % python_lib)

sys.path.append(python_lib)
import traceback

mgr = ServerManager()
site = mgr.Sites[0]

try:

  uri = System.Uri('http://elmcity.blob.core.windows.net/admin/python_library.zip')
  ElmcityUtils.FileUtils.UnzipFromUrlToDirectory(uri, local_storage)

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
      log.write('permissions: %s' % traceback.print_exc() )   
      
  def set_config_attr(element, attr_name, attr_value):
    log.write('%s/@%s was %s\n' % ( element.ElementTagName, attr_name, element.GetAttributeValue(attr_name) ) )
    element.SetAttributeValue(attr_name, attr_value)
    log.write('%s/@%s is %s\n' % ( element.ElementTagName, attr_name, element.GetAttributeValue(attr_name) ) )  

  log.write('...changing permissions on local_storage...\n')
  inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
  propagation = PropagationFlags.None
  set_permissions(local_storage, 'Administrators', FileSystemRights.FullControl, inheritance, propagation, AccessControlType.Allow);
  set_permissions(local_storage, 'NETWORK SERVICE', FileSystemRights.Write | FileSystemRights.ReadAndExecute, inheritance, propagation, AccessControlType.Allow);
 
  
  log.write('...sending failed requests to local storage...\n')

  try:
    log.write('from: %s\n' % site.TraceFailedRequestsLogging.Attributes['Directory'].Value)
    site.TraceFailedRequestsLogging.SetAttributeValue('Directory', local_storage)
    mgr.CommitChanges()
    log.write('to: %s\n' % site.TraceFailedRequestsLogging.Attributes['Directory'].Value)
  except:
    log.write('redirect failed requests: %s\n' % traceback.print_exc() )

  log.write('...unlocking config section ...\n')

  try:
    cfg = mgr.GetApplicationHostConfiguration()
    section = cfg.RootSectionGroup.SectionGroups['system.webServer'].SectionGroups['security'].Sections.Item['dynamicipsecurity']
    section.OverrideModeDefault = 'Allow'
    mgr.CommitChanges()
  except:
    log.write('unlock config: %s\n' % traceback.print_exc() )

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
    log.write('configuring dynamic ip restrictions: %s\n' % traceback.print_exc() )
    
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
    log.write(traceback.print_exc())  

  log.write('...stopping at UTC %s...\n' % System.DateTime.UtcNow.ToString())

except:
  log.write('traceback %s' % traceback.print_exc())
  
log.close()

