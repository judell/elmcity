<%@ Page Title="" Language="C#" MasterPageFile="~/Views/Shared/AltCSS.Master" Inherits="System.Web.Mvc.ViewPage" %>
<asp:Content ID="Content1" ContentPlaceHolderID="TitleContent" runat="server">
<%= ViewData["title"] %>
</asp:Content>
<asp:Content ID="Content2" ContentPlaceHolderID="MainContent" runat="server">

<% var id = ViewData["id"]; Response.Write(ElmcityUtils.HttpUtils.FetchUrl(new Uri("http://elmcity.blob.core.windows.net/admin/hubfiles.tmpl")).DataAsString().Replace("__ID__", id.ToString())); %>
</asp:Content>
