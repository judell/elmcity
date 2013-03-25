<%@ Page Language="C#" MasterPageFile="~/Views/Shared/Site.Master"  Inherits="System.Web.Mvc.ViewPage"%>
<asp:Content ID="errorTitle" ContentPlaceHolderID="TitleContent" runat="server">
    Oops.
</asp:Content>

<asp:Content ID="errorContent" ContentPlaceHolderID="MainContent" runat="server">
    <h2>
        Something went wrong, sorry. It's been logged and reported.
    </h2>
</asp:Content>

