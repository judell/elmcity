function gup( name )
  {  
  name = name.replace(/[\[]/,"\\\[").replace(/[\]]/,"\\\]");  
  var regexS = "[\\?&]"+name+"=([^&#]*)";  
  var regex = new RegExp( regexS );  
  var results = regex.exec( window.location.href );   
  if( results == null )    
    return "";  
  else    
    return results[1].replace(/%20/,' ');
  }


var src = $('iframe').attr('src');

if ( gup('view') )
  {
  src = src + '?view=' + gup('view');
  $('#elmcity_frame').empty();
  var html = '<iframe style="border: 1px,single,black;" src="__SRC__" width="90%" height="1800"></iframe>'.replace('__SRC__', src);
  $('#elmcity_frame').html(html);
  }
