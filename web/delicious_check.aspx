<%@ Page Language="C#"  Inherits="System.Web.Mvc.ViewPage" %>
<html>
<head>
<title>check delicious account></title>
<link href="http://elmcity.blob.core.windows.net/admin/elmcity.css" rel="stylesheet" type="text/css" />
<script type="text/javascript">
function GetQueryStringValue(key, default_) 
  {
  if (default_ == null) default_ = "";
  key = key.replace(/[\[]/, "\\\[").replace(/[\]]/, "\\\]");
  var regex = new RegExp("[\\?&]" + key + "=([^&#]*)");
  var qs = regex.exec(window.location.href);
  if (qs == null)
     return default_;
  else
    return qs[1];
}

function SetInputValue() 
    {
    document.forms["delicious_check"]["id"].value = GetQueryStringValue("id");
    }

</script>
</head>

<body onload="javascript:SetInputValue()">

<h1>check delicious account</h1>
<form id="delicious_check" action="/delicious_check">
delicious account name: <input name="id" id="id" value="" />
<input type="submit" value="go"/> (this takes a little while...)
</form>


<p>
<%= ViewData["result"] %>
</p>

</body>
</html>





