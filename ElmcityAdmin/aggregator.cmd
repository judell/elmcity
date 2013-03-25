rem Utils

copy c:\\users\\jon\\ElmcityUtils\\*.cs		c:\\users\\jon\\elmcity_info\\elmcityutils
copy c:\\users\\jon\\ElmcityUtils\\*.cs		c:\\users\\jon\\elmcity\\elmcityutils

copy c:\\users\\jon\\dev\\SafeConfigurator.cs c:\\users\\jon\\elmcity\\elmcityutils\Configurator.cs

copy c:\\users\\jon\\ElmcityUtils\\bin\\Debug\\ElmcityUtils.dll c:\\users\\jon\\aptc

rem Aggregator

copy c:\\users\\jon\\ElmcityAggregator\\*.cs c:\\users\\jon\\elmcity_info\\agg
copy c:\\users\\jon\\ElmcityAggregator\\*.cs c:\\users\\jon\\elmcity\\agg

copy c:\\users\\jon\\ElmcityAggregator\\app.config c:\\users\\jon\\elmcity_info\\agg

copy c:\\users\\jon\\ElmcityAggregator\\bin\\Debug\\CalendarAggregator.dll c:\\users\\jon\\aptc

rem CloudService

rem copy c:\\users\\jon\\ElmcityService\\CloudService\ServiceDefinition*	c:\\users\\jon\\elmcity_info\\service
rem copy c:\\users\\jon\\ElmcityService\\CloudService\ServiceConfiguration* c:\\users\\jon\\elmcity_info\\service

rem WorkerRole

copy c:\\users\\jon\\ElmcityService\\WorkerRole\\WorkerRole.cs		c:\\users\\jon\\elmcity_info\\worker
copy c:\\users\\jon\\ElmcityService\\WorkerRole\\WorkerRoleTest.cs	c:\\users\\jon\\elmcity_info\\worker
copy c:\\users\\jon\\ElmcityService\\WorkerRole\\*.config			c:\\users\\jon\\elmcity_info\\worker
copy c:\\users\\jon\\ElmcityService\\WorkerRole\\*.cs				c:\\users\\jon\\elmcity_info\\worker

copy c:\\users\\jon\\ElmcityService\\WorkerRole\\WorkerRole.cs		c:\\users\\jon\\elmcity\\worker
copy c:\\users\\jon\\ElmcityService\\WorkerRole\\WorkerRoleTest.cs	c:\\users\\jon\\elmcity\\worker
copy c:\\users\\jon\\ElmcityService\\WorkerRole\\*.config			c:\\users\\jon\\elmcity\\worker
copy c:\\users\\jon\\ElmcityService\\WorkerRole\\*.cs				c:\\users\\jon\\elmcity\\worker


copy c:\\users\\jon\\ElmcityService\\WorkerRole\\Startup		c:\\users\\jon\\elmcity_info\\worker
copy c:\\users\\jon\\ElmcityService\\WorkerRole\\Startup		c:\\users\\jon\\elmcity\\worker

copy c:\\users\\jon\\ElmcityService\\WorkerRole\\Scripts		c:\\users\\jon\\elmcity_info\\worker
copy c:\\users\\jon\\ElmcityService\\WorkerRole\\Scripts		c:\\users\\jon\\elmcity\\worker

copy c:\\users\\jon\\ElmcityService\\WorkerRole\\bin\Debug\\WorkerRole.dll c:\\users\\jon\\aptc


rem WebRole

copy c:\\users\\jon\\ElmcityService\\WebRole\\*.cs			c:\\users\\jon\\elmcity_info\\web
copy c:\\users\\jon\\ElmcityService\\WebRole\\*.cs			c:\\users\\jon\\elmcity\\web

copy c:\\users\\jon\\ElmcityService\\WebRole\\*.config		c:\\users\\jon\\elmcity_info\\web
copy c:\\users\\jon\\ElmcityService\\WebRole\\*.config		c:\\users\\jon\\elmcity\\web

copy c:\\users\\jon\\ElmcityService\\WebRole\\Controllers\\*.cs		c:\\users\\jon\\elmcity_info\\web
copy c:\\users\\jon\\ElmcityService\\WebRole\\Controllers\\*.cs		c:\\users\\jon\\elmcity\\web

copy c:\\users\\jon\\ElmcityService\\WebRole\\Views\\Shared\\FinalError.aspx	c:\\users\\jon\\elmcity_info\\web
copy c:\\users\\jon\\ElmcityService\\WebRole\\Views\\Shared\\FinalError.aspx	c:\\users\\jon\\elmcity\\web

copy c:\\users\\jon\\ElmcityService\\WebRole\\Startup c:\\users\\jon\\elmcity_info\\web
copy c:\\users\\jon\\ElmcityService\\WebRole\\Startup c:\\users\\jon\\elmcity\\web

copy c:\\users\\jon\\ElmcityService\\WebRole\\Scripts c:\\users\\jon\\elmcity_info\\web
copy c:\\users\\jon\\ElmcityService\\WebRole\\Scripts c:\\users\\jon\\elmcity\\web


copy c:\\users\\jon\\ElmcityService\\WebRole\\bin\\WebRole.dll c:\\users\\jon\\aptc

rem admin

copy c:\\users\\jon\\elmcity_info\\admin\\* c:\\users\\jon\\elmcity\\admin

copy c:\\users\\jon\\dev\\aggregator.cmd c:\\users\\jon\\elmcity_info\\admin

rem doc

rem copy c:\\users\\jon\\elmcity_info\\doc\\* c:\\users\\jon\\elmcity\\doc


rem fusecal

copy c:\\users\\jon\\elmcity_info\\fusecal\\*.py				c:\\users\\jon\\elmcity\\fusecal
copy c:\\users\\jon\\elmcity_info\\fusecal\\ElmcityLib\*.py		c:\\users\\jon\\elmcity\\fusecal\ElmcityLib


