<%@ Page Language="C#" MasterPageFile="~/Views/Shared/Site.Master" Inherits="System.Web.Mvc.ViewPage"  ValidateRequest="false" %>
<asp:Content ID="TitleContent" ContentPlaceHolderID="TitleContent" runat="server"><%= ViewData["title"] %></asp:Content>
<asp:Content ID="indexContent" ContentPlaceHolderID="MainContent" runat="server">

<pre>
<%= ViewData["snapshot"] %>
</pre>

</asp:Content>

