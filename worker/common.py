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


