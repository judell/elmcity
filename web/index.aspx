<%@ Page Language="C#" MasterPageFile="~/Views/Shared/Site.Master" Inherits="System.Web.Mvc.ViewPage" %>

<asp:Content ID="TitleContent" ContentPlaceHolderID="TitleContent" runat="server">
<%= ViewData["title"] %>
</asp:Content>

<asp:Content ID="indexContent" ContentPlaceHolderID="MainContent" runat="server">


<h1 style="text-align:center">the elmcity calendar curation project: 
<a href="http://blog.jonudell.net/2009/04/10/community-calendar-curation-the-startup-guide/">quickstart</a>, 
<a href="http://blog.jonudell.net/elmcity-project-faq/">faq</a>, 
<a href="http://delicious.com/judell/elmcity+azure">backstory</a>, 
<a href="http://blog.jonudell.net/elmcityazure-project-status/">changelog</a>, 
<a href="http://friendfeed.com/rooms/elmcity">curators room</a>  
</h1>

<table style="width:100%">
<tr><td valign="top">

<h2 style="text-align:center">where</h2>

<%= ViewData["where_summary"] %>

</td>

<td valign="top">

<h2 style="text-align:center">what</h2>

<%= ViewData["what_summary"] %>

</td></tr>
</table>

<p>Version <%=ViewData["version"] %></p>

</asp:Content>



