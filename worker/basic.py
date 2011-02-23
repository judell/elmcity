import sys, os

result = 'path: %s, pythonpath: %s' % ( os.path.realpath('.'), ','.join(sys.path) )