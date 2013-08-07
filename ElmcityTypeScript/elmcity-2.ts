declare var jQuery;
var $j = jQuery;

try
{
    $j = jQuery.noConflict();
}
catch (e)
{
    console.log('jQuery not here yet');
}

interface String {
    startsWith(s: string): boolean;
    contains(s: string): boolean;
}

class ElmCityParameters {
    static days: string = "";
    static eventsonly:string = "";
    static from:string = "";
    static jsurl:string = "";
    static host_dom_element:string = "";
    static mobile:string = "";
    static template:string = "";
    static theme:string = "";
    static to:string = "";
    static view:string = "";
}

class ElmCity {
    static is_ready = false;
    static injecting = false;
    static host = 'http://elmcity.cloudapp.net';
    static blobhost = "http://elmcity.blob.core.windows.net";
    static anchor_names = new Array<string>();
    static today = new Date();
    static last_day = new Date();
    static is_mobile = false;
    static is_mobile_declared = false;
    static is_mobile_detected = false;
    static is_eventsonly = false;
    static is_theme = false;
    static is_view = false;
    static is_sidebar = true;
    static top_method = "auto"; // for use in position_sidebar
    static top_offset = 0;
    static top_element = 'elmcity_sidebar_top';
    static theme = "";
    static template = "";
    static events_url = "";
    static elmcity_id = "";
    static host_dom_element = "";
    static view = "";
    static redirected_hubs = ['AnnArborChronicle'];
    static days = 0;
    static current_event_id = "";

    constructor() {
    }

    static ready() {

        if (ElmCity.injecting == true && ElmCity.is_ready == false)
            return;

        ElmCity.elmcity_id = Utils.get_elmcity_id();
        
        if (ElmCity.injecting == false)
            ElmCity.events_url = location.href;   
        
        Utils.get_url_params();

        Utils.process_url_params();

        Utils.apply_style_params();

        $j(window).scroll(function (event) {
            ElmCity.scroll(event);
        });


        Utils.remember_or_forget_days();

        if (!ElmCity.is_sidebar)
            return;

        ElmCity.setup_datepicker();

        $j('body').attr('onload', '');  // until that goes away in the template

    }

    static inject() {

        if (typeof (jQuery) != 'function') {
            Utils.prototype.jQuerify('http://ajax.aspnetcdn.com/ajax/jQuery/jquery-1.7.2.min.js', function () {
                if (typeof jQuery != 'function') {
                    console.log('Could not load jQuery');
                }
                else {
                    $j = jQuery.noConflict();
                    console.log('Loaded jQuery');
                }
            });
        } else {
            $j = jQuery.noConflict();
            console.log('jQuery already here');
        }

        $j.getScript(ElmCity.blobhost + '/admin/jquery.cookie.js');

        $j.getScript('http://ajax.aspnetcdn.com/ajax/jquery.ui/1.8.20/jquery-ui.min.js');

        var jquery_ui_theme_uri = "http://ajax.aspnetcdn.com/ajax/jquery.ui/1.8.20/themes/smoothness/jquery-ui.css";
        if ( Utils.css_exists(jquery_ui_theme_uri) == false )
            $j('head').append('<link type="text/css" href="' + jquery_ui_theme_uri + '" rel="stylesheet" />');

        var theme_uri = ElmCity.host + '/get_css_theme?theme_name=' + ElmCity.theme;
        if ( Utils.css_exists(theme_uri) == false )
            $j('head').append('<link type="text/css" href="' + theme_uri + '" rel="stylesheet" />');

        var responsive_theme_uri = ElmCity.blobhost + '/admin/responsive.css';
        if ( Utils.css_exists(responsive_theme_uri) == false )
            $j('head').append('<link type="text/css" href="' + responsive_theme_uri + '" rel="stylesheet" />');

        $j.ajax({
            url: ElmCity.events_url,
            cache: false
        }).done(function (html) {
            $j('#' + ElmCity.host_dom_element).html(html);
            ElmCity.is_ready = true;
            ElmCity.ready();
            });
    }

    static scroll(event) {
        
        if (ElmCity.is_mobile || ElmCity.is_eventsonly)
            return;
        if ($j('#sidebar').css('position') != 'fixed') // unframed, no fixed elements
            ElmCity.position_sidebar();
        var date_str = ElmCity.find_current_name().replace('d', '');
        var parsed = Utils.parse_yyyy_mm_dd(date_str);
        ElmCity.highlight_date(parseInt(parsed['year']), parseInt(parsed['month']), parseInt(parsed['day']));
    }

    static position_sidebar() {
        try
        {
            var top_elt_bottom = $j('#' + ElmCity.top_element)[0].getClientRects()[0].bottom;
        }
        catch (e)
        {
            console.log(e.message);
            top_elt_bottom = 0;
        }

        if (top_elt_bottom <= 0)
            $j('#sidebar').css('top', $j(window).scrollTop() - ElmCity.top_offset + 'px');
        else
            $j('#sidebar').css('top', ElmCity.top_method);
    }

    static find_current_name() : string {
        if (ElmCity.is_mobile || ElmCity.is_eventsonly)
            return;

        try
        {
            var before = [];
            var datepicker_top = $j("#datepicker")[0].getClientRects()[0].top;
            var datepicker_bottom = $j("#datepicker")[0].getClientRects()[0].bottom;
            var datepicker_height = datepicker_bottom - datepicker_top;
            var datepicker_center = datepicker_top + (datepicker_height / 2);
            var anchors = ElmCity.day_anchors();
            for (var i = 0; i < anchors.length; i++)
            {
                var anchor = anchors[i];
                var anchor_top = anchor.getClientRects()[0].top;
                if (anchor_top < datepicker_center)
                    before.push(anchor.name);
                else
                    break;
            }
            var ret = before[before.length - 1];
            if (typeof ret == 'undefined')
                ret = anchors[0].name;
        }
        catch (e)
        {
            console.log("find_current_name: " + e.message);
        }
        return ret;
    }

    static find_last_day() {
        try
        {
            var last_anchor = ElmCity.anchor_names[ElmCity.anchor_names.length - 1];
            var parsed = Utils.parse_yyyy_mm_dd(last_anchor.replace('d', ''));
            return new Date(parseInt(parsed['year']), parseInt(parsed['month']) - 1, parseInt(parsed['day']));
        }
        catch (e)
        {
            return new Date();
        }
    }

    static highlight_date(year: number, month: number, day: number) {
        var date = $j('#datepicker').datepicker('getDate');
        var current_date = $j('td > a[class~=ui-state-active]');
        current_date.css('font-weight', 'normal');
        $j('#datepicker').datepicker('setDate', new Date(year, month - 1, day));
        //var td = $j('td[class=ui-datepicker-current-day] > a[class~=ui-state-active]');
        //var td = $j('td > a[class~=ui-state-active]');
        current_date = $j('td > a[class~=ui-state-active]');
        current_date.css('font-weight', 'bold');
    }

    static day_anchors(): Array<HTMLAnchorElement> {
        return $j('a[name^="d"]');
    }

    static setup_datepicker() {
        if (ElmCity.is_eventsonly || ElmCity.is_mobile)
            return;

        ElmCity.prep_day_anchors_and_last_day();

        $j('#datepicker').datepicker({
            onSelect: function (dateText, inst) { ElmCity.go_day(dateText); },
            onChangeMonthYear: function (year: number, month: number, inst) { ElmCity.go_month(year.toString(), month.toString()); },
            minDate: ElmCity.today,
            maxDate: ElmCity.last_day,
            hideIfNoPrevNext: true,
            beforeShowDay: ElmCity.maybe_show_day
        });

        ElmCity.highlight_date(ElmCity.today.getFullYear(), ElmCity.today.getMonth() + 1, ElmCity.today.getDate());

        if ($j('#sidebar').css('position') != 'fixed') { // unframed, no fixed elements
            ElmCity.position_sidebar()
            $j('#sidebar').css('visibility', 'visible');
            $j('#datepicker').css('visibility', 'visible');
            $j('#tags').css('visibility', 'visible');
        }

    }

    static prep_day_anchors_and_last_day() {
        var anchors = ElmCity.day_anchors();
        ElmCity.anchor_names = ElmCity.get_anchor_names(anchors);
        ElmCity.last_day = ElmCity.find_last_day();
    }

    static get_anchor_names(anchors: Array<HTMLAnchorElement>): Array<string> {
        var anchor_names = [];
        for (var i = 0; i < anchors.length; i++) {
            anchor_names.push(anchors[i].name);
        }
        return anchor_names;
    }

    static go_day(date_str: string) {
        var parsed = Utils.parse_mm_dd_yyyy(date_str)
        var year = parsed['year'];
        var month = parsed['month'];
        var day = parsed['day'];
        var id = 'd' + year + month + day;
        Utils.scroll_to_element(id);
    }

    static go_month(year: string, month: string) {
        month = Utils.maybe_zero_pad(month, 2);
        var id = $j('h1[id^="d' + year + month + '"]').attr('id')
        Utils.scroll_to_element(id);
    }

    static maybe_show_day(date: Date): Array {
        var year = date.getFullYear();
        var month = date.getMonth() + 1;
        var day = date.getDate();
        var s_year = year.toString();
        var s_month = Utils.maybe_zero_pad(month.toString(), 2);
        var s_day = Utils.maybe_zero_pad(day.toString(), 2);
        var date_str = "d" + s_year + s_month + s_day;
        var show = $j.inArray(date_str, ElmCity.anchor_names) == -1 ? false : true;
        var style = (show == false) ? "ui-datepicker-unselectable ui-state-disabled" : "";
        return [show, style]
    }

    static make_view_path() {
        var path;
        if (ElmCity.redirected_hubs.indexOf(ElmCity.elmcity_id) == -1)
        {
            path =  ElmCity.host + '/' + ElmCity.elmcity_id + '/?view=' + encodeURIComponent(ElmCity.view);
        }
        else
        {
            path = ElmCity.host + '/html?view=' + encodeURIComponent(ElmCity.view);
        }

        return path;
    }

    static make_cookie_name_from_view() {
        var view = ElmCity.view;
        view = view.replace(',', '_');
        view = view.replace('-', '_minus_');
        var cookie_name = 'elmcity_' + view + '_days';
        return cookie_name;
    }

}

class Utils {

    jQuerify(url, success) {
        var script = document.createElement('script');
        script.src = url;
        var head = document.getElementsByTagName('head')[0];
        var done = false;
        script.onload = script.onreadystatechange =
        function () {
            if (!done && (!this.readyState || this.readyState == 'loaded' || this.readyState == 'complete'))
            {
                done = true;
                success();
                script.onload = script.onreadystatechange = null;
                head.removeChild(script);
            }
        };
        head.appendChild(script);
    }

    static apply_style_params() {
        if (Utils.gup('datestyle') != '')
            Utils.apply_json_css($j, '.ed', 'datestyle');

        if (Utils.gup('itemstyle') != '')
            Utils.apply_json_css($j, '.bl', 'itemstyle');

        if (Utils.gup('titlestyle') != '')
            Utils.apply_json_css($j, '.ttl', 'titlestyle');

        if (Utils.gup('linkstyle') != '')
            Utils.apply_json_css($j, '.ttl a', 'linkstyle');

        if (Utils.gup('dtstartstyle') != '')
            Utils.apply_json_css($j, '.st', 'dtstartstyle');

        if (Utils.gup('sd') != '')
            Utils.apply_json_css($j, '.sd', 'sd');

        if (Utils.gup('atc') != '')
            Utils.apply_json_css($j, '.atc', 'atc');

        if (Utils.gup('cat') != '')
            Utils.apply_json_css($j, '.cat', 'cat');

        if (Utils.gup('sourcestyle') != '')
            Utils.apply_json_css($j, '.src', 'sourcestyle');
    }

    static get_url_params() {
        if (Utils.gup('days') != '')              ElmCityParameters.days = Utils.gup('days')                
        if (Utils.gup('eventsonly') != '')        ElmCityParameters.eventsonly = Utils.gup('eventsonly');
        if (Utils.gup('from') != '')              ElmCityParameters.from = Utils.gup('from');
        if (Utils.gup('jsurl') != '')             ElmCityParameters.jsurl = Utils.gup('jsurl');
        if (Utils.gup('host_dom_element') != '')  ElmCityParameters.host_dom_element = Utils.gup('host_dom_element');
        if (Utils.gup('mobile') != '')            ElmCityParameters.mobile = Utils.gup('mobile');
        if (Utils.gup('template') != '')          ElmCityParameters.template = Utils.gup('template');
        if (Utils.gup('theme') != '')             ElmCityParameters.theme = Utils.gup('theme');
        if (Utils.gup('to') != '')                ElmCityParameters.to = Utils.gup('to');
        if (Utils.gup('view') != '')              ElmCityParameters.view = Utils.gup('view');
    }

    static process_url_params() {

        for (var p in ElmCityParameters)
        {
            if (p != undefined && ElmCityParameters[p] != '')
                ElmCity.events_url = Utils.add_href_arg(ElmCity.events_url, p, ElmCityParameters[p]);
        }

        ElmCity.is_theme =              ElmCityParameters.theme != '';
        ElmCity.is_view =               ElmCityParameters.view != '';
        ElmCity.is_eventsonly =         ElmCityParameters.eventsonly.startsWith('y');
        ElmCity.is_mobile_declared =    ElmCityParameters.mobile.startsWith('y');
        ElmCity.is_mobile_detected = $j('#mobile_detected').text().trim() == "__MOBILE_DETECTED__";
        ElmCity.is_mobile = ElmCity.is_mobile_declared || ElmCity.is_mobile_detected;
        ElmCity.is_sidebar = (ElmCity.is_mobile == false) && (ElmCity.is_eventsonly == false);

        if (Utils.gup('tags').startsWith('n'))
            $j('.cat').remove();

        if (ElmCity.is_view && ElmCity.is_sidebar)
            try
            {
                var href = $j('#subscribe').attr('href');
                href = href.replace('__VIEW__', Utils.gup('view'));
                $j('#subscribe').attr('href', href);
                $j('#subscribe').text('subscribe');
            }
            catch (e)
            {
                console.log(e.description);
            }

        if (Utils.gup('timeofday') == 'no')
            $j('.timeofday').remove();
    }

    static parse_yyyy_mm_dd(date_str) {
        var match = /(\d{4,4})(\d{2,2})(\d{2,2})/.exec(date_str);
         return {
            year: match[1],
            month: Utils.maybe_zero_pad(match[2], 2),
            day: Utils.maybe_zero_pad(match[3], 2)
        }
    }

    static parse_mm_dd_yyyy(date_str) {
        var match = /(\d{2,2})\/(\d{2,2})\/(\d{4,4})/.exec(date_str);
        return {
            month: Utils.maybe_zero_pad(match[1], 2),
            day: Utils.maybe_zero_pad(match[2], 2),
            year: match[3]
        }
    }

    static scroll_to_element(id) {
        window.scrollTo(0, $j('#' + id).offset().top);
    }

    static get_elmcity_id() {
        return $j('#elmcity_id').text().trim();
    }

    static get_summary(id) {
        var elt = $j('#' + id);
        var summary = $j('#' + id + ' .ttl span').text();
        if (summary == '')
            summary = $j('#' + id + ' .ttl a').text();
        return summary;
    }

    static get_dtstart(id) {
        return $j('#' + id + ' .st').attr('content');
    }

    static maybe_zero_pad(str: string, len: number): string {
        while (str.length < len) str = "0" + str;
        return str;
    }

    static gup(name) {
        name = name.replace(/[\[]/, "\\\[").replace(/[\]]/, "\\\]");
        var regexS = "[\\?&]" + name + "=([^&#]*)";
        var regex = new RegExp(regexS);
        var results = regex.exec(window.location.href);
        if (results == null)
            return "";
        else
            return results[1].replace(/%20/, ' ');
    }

    static remove_href_arg(href, name) {
        var pat = eval('/[\?&]*' + name + '=[^&]*/');
        href = href.replace(pat, '');
        if ((!href.contains('?')) && href.contains('&'))
            href = href.replaceAt(href.indexOf('&'), '?');
        return href;
    }

    static add_href_arg(href:string, name:string, value:string): string {
        href = Utils.remove_href_arg(href, name);
        if (href.contains('?'))
            href = href + '&' + name + '=' + value;
        else
        {
            href = href + '?' + name + '=' + value;
        }
        return href;
    }

    static propagate_href_args(path: string): string {

        if (Utils.gup('theme') != '')
            path = Utils.add_href_arg(path, 'theme', Utils.gup('theme'));

        if (Utils.gup('count') != '')
            path = Utils.add_href_arg(path, 'count', Utils.gup('count'));

        if (Utils.gup('mobile') != '')
            path = Utils.add_href_arg(path, 'mobile', Utils.gup('mobile'));

        if (Utils.gup('eventsonly') != '')
            path = Utils.add_href_arg(path, 'eventsonly', Utils.gup('eventsonly'));

        if (Utils.gup('template') != '')
            path = Utils.add_href_arg(path, 'template', Utils.gup('template'));

        if (Utils.gup('jsurl') != '')
            path = Utils.add_href_arg(path, 'jsurl', Utils.gup('jsurl'));

        return path;

    }

    static apply_json_css(jquery, element, style) {
        try
        {
            var style = decodeURIComponent(Utils.gup(style));
            style = style.replace(/'/g, '"');
            jquery(element).css(JSON.parse(style));
        }
        catch (e)
        {
            console.log(e.message);
        }
    }

    static remember_or_forget_days() {
        var view = Utils.gup('view');
        var days = Utils.gup('days');

        if (days != '')
            Utils.remember_days(view, days);
        else
            Utils.forget_days(view);
    }

    static remember_days(view, days) {
        try
        {
            var cookie_name = Utils.make_cookie_name_from_view(view);
            $j.cookie(cookie_name, days);
        }
        catch (e)
        {
            console.log(e.message);
        }
    }

    static forget_days(view) {
        try
        {
            var cookie_name = Utils.make_cookie_name_from_view(view);
            $j.removeCookie(cookie_name);
        }
        catch (e)
        {
            console.log(e.message);
        }
    }

    static make_cookie_name_from_view(view) {
        if (view == 'all')
            view = '';
        view = view.replace(',', '_');
        view = view.replace('-', '_minus_');
        var cookie_name = 'elmcity_' + view + '_days';
        return cookie_name;
    }

    static get_add_to_cal_url(id, flavor) {
        var elt = $j('#' + id);
        var start = elt.find('.st').attr('content');
        var end = ''; // for now
        var url = elt.find('.ttl').find('a').attr('href');
        var summary = Utils.get_summary(id);
        var description = elt.find('.src').text();
        var location = ''; // for now

        var service_url = ElmCity.host + '/add_to_cal?elmcity_id=' + ElmCity.elmcity_id +
            '&flavor=' + flavor +
            '&start=' + encodeURIComponent(start) +
            '&end=' + end +
            '&summary=' + encodeURIComponent(summary) +
            '&url=' + encodeURIComponent(url) +
            '&description=' + encodeURIComponent(description) +
            '&location=' + location;
        return service_url;
    }

    static add_to_google(id) {
        try
        {
            var service_url = Utils.get_add_to_cal_url(id, 'google');
            $j('.menu').remove();
            //  console.log('redirecting to ' + service_url);
            //  location.href = service_url;
            window.open(service_url, "add to google");
        }
        catch (e)
        {
            console.log(e.message);
        }
    }

    static add_to_hotmail(id) {
        var service_url = Utils.get_add_to_cal_url(id, 'hotmail');
        $j('.menu').remove();
        location.href = service_url;
    }

    static add_to_ical(id) {
        var service_url = Utils.get_add_to_cal_url(id, 'ical');
        $j('.menu').remove();
        location.href = service_url;
    }

    static add_to_facebook(id) {
        var service_url = Utils.get_add_to_cal_url(id, 'facebook');
        $j('.menu').remove();
        location.href = service_url;
    }

    static dismiss_menu(id) {
        var elt = $j('#' + id);
        elt.find('.menu').remove();
    }

    static active_description(description) {
        var quoted_id = '\'' + ElmCity.current_event_id + '\'';
        var x = '<span><a title="hide description" href="javascript:hide_desc(' + quoted_id + ')">[x]</a> </span>';
        var s = '<div style="overflow:hidden;text-indent:0" id="' + ElmCity.current_event_id + '_desc' + '">' + description + ' ' + x + '</div>';
        var elt = $j('#' + ElmCity.current_event_id);
        s = s.replace('<br><br>', '<br>');
        elt.append(s);
    }

    static css_exists(uri: string): boolean {
        var stylesheets = $j('link');
        for (var i = 0; i < stylesheets.length; i++)
            if (stylesheets[i].href == uri)
                return true;
        return false;
    }

}

// todo: consider encapsulating these in the ElmCity namespace when the generator provides qualified names

function add_to_cal(id) {
    var elt = $j('#' + id);
    var quoted_id = '\'' + id + '\'';
    elt.find('.menu').remove();
    elt.append(
        '<ul class="menu">' +
        '<li><a title="add this event to your Google calendar" href="javascript:Utils.add_to_google(' + quoted_id + ')">add to Google Calendar</a></li>' +
        '<li><a title="add this event to your Hotmail calendar" href="javascript:Utils.add_to_hotmail(' + quoted_id + ')">add to Hotmail Calendar</a></li>' +
        '<li><a title="add to your Outlook, Apple iCal, or other iCalendar-aware desktop calendar" href="javascript:Utils.add_to_ical(' + quoted_id + ')">add to iCal</a></li>' +
        '<li><a title="add to Facebook (remind yourself and invite friends with 1 click!)" href="javascript:Utils.add_to_facebook(' + quoted_id + ')">add to Facebook</a></li>' +
        '<li><a title="dismiss this menu" href="javascript:Utils.dismiss_menu(' + quoted_id + ')">cancel</a></li>' +
        '</ul>'
        );
}

function show_desc(id) {
    var quoted_id = '\'' + id + '\'';

    $j('#' + id + ' .sd').css('display', 'none');
    $j('#' + id + ' .atc').css('display', 'none');

    var _dtstart = Utils.get_dtstart(id);
    var _title = Utils.get_summary(id);
    var url = ElmCity.host + '/' + ElmCity.elmcity_id + '/description_from_title_and_dtstart?title=' + encodeURIComponent(_title) + '&dtstart=' + _dtstart + '&jsonp=Utils.active_description';

    ElmCity.current_event_id = id;

    $j.getScript(url);
}

function hide_desc(id) {
    var quoted_id = '\'' + id + '\'';

    $j('#' + id + '_desc').remove();
    $j('#' + id + ' .sd').css('display', 'inline');
    $j('#' + id + ' .atc').css('display', 'inline');
}

function show_view(view) {

    if (view == undefined)
    {
        var selected = $j('#tag_select option:selected').val();
        ElmCity.view = selected.replace(/\s*\((\d+)\)/, '');
        if (ElmCity.view == 'all')
            ElmCity.view = '';
    }
    else
    {
        ElmCity.view = view;
    }

    try
    {
        var days_cookie_name = ElmCity.make_cookie_name_from_view();
        var days_cookie_value = $j.cookie(days_cookie_name);
        if (typeof (days_cookie_value) != 'undefined')
        {
            ElmCity.days = days_cookie_value;
        }
        else
        {
            ElmCity.days = 0;
        }
    }
    catch (e)
    {
        console.log(e.message);
    }

    /*
    if (ElmCity.injecting)
    {
        ElmCity.events_url = Utils.add_href_arg(ElmCity.events_url, 'view', ElmCity.view);
        ElmCity.inject();
    }
    else*/
    {
        var path = ElmCity.make_view_path();
        path = Utils.propagate_href_args(path);
        location.href = path;
    }

}

// remove this when all template dependencies are gone
function on_load() {
}

$j(document).ready(ElmCity.ready);





