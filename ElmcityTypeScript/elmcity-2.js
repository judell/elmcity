var $j = jQuery;

try  {
    $j = jQuery.noConflict();
} catch (e) {
    console.log('jQuery not here yet');
}

var ElmCityParams = (function () {
    function ElmCityParams() {
    }
    ElmCityParams.days = "";
    ElmCityParams.eventsonly = "";
    ElmCityParams.from = "";
    ElmCityParams.jsurl = "";
    ElmCityParams.host_dom_element = "";
    ElmCityParams.mobile = "";
    ElmCityParams.tags = "";
    ElmCityParams.taglist = "";
    ElmCityParams.template = "";
    ElmCityParams.theme = "";
    ElmCityParams.timeofday = "";
    ElmCityParams.to = "";
    ElmCityParams.view = "";
    return ElmCityParams;
})();

var ElmCity = (function () {
    function ElmCity() {
    }
    ElmCity.ready = function () {
        if (ElmCity.injecting == true && ElmCity.is_ready == false)
            return;

        ElmCity.elmcity_id = Utils.get_elmcity_id();

        if (ElmCity.injecting == false)
            ElmCity.events_url = location.href;

        Utils.get_url_params();

        Utils.process_params();

        var base_url = ElmCity.host + '/' + ElmCity.elmcity_id + '/html';

        ElmCity.events_url = Utils.propagate_internal_params(base_url);

        //ElmCity.events_url = Utils.propagate_href_args(ElmCity.events_url);
        Utils.apply_style_params();

        $j(window).scroll(function (event) {
            ElmCity.scroll(event);
        });

        Utils.remember_or_forget_days();

        if (!ElmCity.is_sidebar)
            return;

        ElmCity.setup_datepicker();

        $j('body').attr('onload', '');
    };

    ElmCity.inject = function () {
        if (typeof (jQuery) != 'function') {
            Utils.prototype.jQuerify('http://ajax.aspnetcdn.com/ajax/jQuery/jquery-1.7.2.min.js', function () {
                if (typeof jQuery != 'function') {
                    console.log('Could not load jQuery');
                } else {
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
        if (Utils.css_exists(jquery_ui_theme_uri) == false)
            $j('head').append('<link type="text/css" href="' + jquery_ui_theme_uri + '" rel="stylesheet" />');

        var theme_uri = ElmCity.host + '/get_css_theme?theme_name=' + ElmCityParams.theme;
        if (Utils.css_exists(theme_uri) == false)
            $j('head').append('<link type="text/css" href="' + theme_uri + '" rel="stylesheet" />');

        var responsive_theme_uri = ElmCity.blobhost + '/admin/responsive.css';
        if (Utils.css_exists(responsive_theme_uri) == false)
            $j('head').append('<link type="text/css" href="' + responsive_theme_uri + '" rel="stylesheet" />');

        $j.ajax({
            url: ElmCity.events_url,
            cache: false
        }).done(function (html) {
            $j('#' + ElmCity.host_dom_element).html(html);
            ElmCity.is_ready = true;
            ElmCity.ready();
        });
    };

    ElmCity.scroll = function (event) {
        if (ElmCity.is_mobile || ElmCity.is_eventsonly)
            return;
        if ($j('#sidebar').css('position') != 'fixed')
            ElmCity.position_sidebar();
        var date_str = ElmCity.find_current_name().replace('d', '');
        var parsed = Utils.parse_yyyy_mm_dd(date_str);
        ElmCity.highlight_date(parseInt(parsed['year']), parseInt(parsed['month']), parseInt(parsed['day']));
    };

    ElmCity.position_sidebar = function () {
        try  {
            var top_elt_bottom = $j('#' + ElmCity.top_element)[0].getClientRects()[0].bottom;
        } catch (e) {
            console.log(e.message);
            top_elt_bottom = 0;
        }

        if (top_elt_bottom <= 0)
            $j('#sidebar').css('top', $j(window).scrollTop() - ElmCity.top_offset + 'px'); else
            $j('#sidebar').css('top', ElmCity.top_method);
    };

    ElmCity.find_current_name = function () {
        if (ElmCity.is_mobile || ElmCity.is_eventsonly)
            return;

        try  {
            var before = [];
            var datepicker_top = $j("#datepicker")[0].getClientRects()[0].top;
            var datepicker_bottom = $j("#datepicker")[0].getClientRects()[0].bottom;
            var datepicker_height = datepicker_bottom - datepicker_top;
            var datepicker_center = datepicker_top + (datepicker_height / 2);
            var anchors = ElmCity.day_anchors();
            for (var i = 0; i < anchors.length; i++) {
                var anchor = anchors[i];
                var anchor_top = anchor.getClientRects()[0].top;
                if (anchor_top < datepicker_center)
                    before.push(anchor.name); else
                    break;
            }
            var ret = before[before.length - 1];
            if (typeof ret == 'undefined')
                ret = anchors[0].name;
        } catch (e) {
            console.log("find_current_name: " + e.message);
        }
        return ret;
    };

    ElmCity.find_last_day = function () {
        try  {
            var last_anchor = ElmCity.anchor_names[ElmCity.anchor_names.length - 1];
            var parsed = Utils.parse_yyyy_mm_dd(last_anchor.replace('d', ''));
            return new Date(parseInt(parsed['year']), parseInt(parsed['month']) - 1, parseInt(parsed['day']));
        } catch (e) {
            return new Date();
        }
    };

    ElmCity.highlight_date = function (year, month, day) {
        var date = $j('#datepicker').datepicker('getDate');
        var current_date = $j('td > a[class~=ui-state-active]');
        current_date.css('font-weight', 'normal');
        $j('#datepicker').datepicker('setDate', new Date(year, month - 1, day));

        //var td = $j('td[class=ui-datepicker-current-day] > a[class~=ui-state-active]');
        //var td = $j('td > a[class~=ui-state-active]');
        current_date = $j('td > a[class~=ui-state-active]');
        current_date.css('font-weight', 'bold');
    };

    ElmCity.day_anchors = function () {
        return $j('a[name^="d"]');
    };

    ElmCity.setup_datepicker = function () {
        if (ElmCity.is_eventsonly || ElmCity.is_mobile)
            return;

        ElmCity.prep_day_anchors_and_last_day();

        $j('#datepicker').datepicker({
            onSelect: function (dateText, inst) {
                ElmCity.go_day(dateText);
            },
            onChangeMonthYear: function (year, month, inst) {
                ElmCity.go_month(year.toString(), month.toString());
            },
            minDate: ElmCity.today,
            maxDate: ElmCity.last_day,
            hideIfNoPrevNext: true,
            beforeShowDay: ElmCity.maybe_show_day
        });

        ElmCity.highlight_date(ElmCity.today.getFullYear(), ElmCity.today.getMonth() + 1, ElmCity.today.getDate());

        if ($j('#sidebar').css('position') != 'fixed') {
            ElmCity.position_sidebar();
            $j('#sidebar').css('visibility', 'visible');
            $j('#datepicker').css('visibility', 'visible');
            $j('#tags').css('visibility', 'visible');
        }
    };

    ElmCity.prep_day_anchors_and_last_day = function () {
        var anchors = ElmCity.day_anchors();
        ElmCity.anchor_names = ElmCity.get_anchor_names(anchors);
        ElmCity.last_day = ElmCity.find_last_day();
    };

    ElmCity.get_anchor_names = function (anchors) {
        var anchor_names = [];
        for (var i = 0; i < anchors.length; i++) {
            anchor_names.push(anchors[i].name);
        }
        return anchor_names;
    };

    ElmCity.go_day = function (date_str) {
        var parsed = Utils.parse_mm_dd_yyyy(date_str);
        var year = parsed['year'];
        var month = parsed['month'];
        var day = parsed['day'];
        var id = 'd' + year + month + day;
        Utils.scroll_to_element(id);
    };

    ElmCity.go_month = function (year, month) {
        month = Utils.maybe_zero_pad(month, 2);
        var id = $j('h1[id^="d' + year + month + '"]').attr('id');
        Utils.scroll_to_element(id);
    };

    ElmCity.maybe_show_day = function (date) {
        var year = date.getFullYear();
        var month = date.getMonth() + 1;
        var day = date.getDate();
        var s_year = year.toString();
        var s_month = Utils.maybe_zero_pad(month.toString(), 2);
        var s_day = Utils.maybe_zero_pad(day.toString(), 2);
        var date_str = "d" + s_year + s_month + s_day;
        var show = $j.inArray(date_str, ElmCity.anchor_names) == -1 ? false : true;
        var style = (show == false) ? "ui-datepicker-unselectable ui-state-disabled" : "";
        return [show, style];
    };

    ElmCity.make_cookie_name_from_view = function () {
        var view = ElmCityParams.view;
        view = view.replace(',', '_');
        view = view.replace('-', '_minus_');
        var cookie_name = 'elmcity_' + view + '_days';
        return cookie_name;
    };
    ElmCity.is_ready = false;
    ElmCity.injecting = false;
    ElmCity.host = 'http://elmcity.cloudapp.net';
    ElmCity.blobhost = "http://elmcity.blob.core.windows.net";
    ElmCity.anchor_names = new Array();
    ElmCity.today = new Date();
    ElmCity.last_day = new Date();
    ElmCity.is_mobile = false;
    ElmCity.is_mobile_declared = false;
    ElmCity.is_mobile_detected = false;
    ElmCity.is_eventsonly = false;
    ElmCity.is_theme = false;
    ElmCity.is_view = false;
    ElmCity.is_sidebar = true;
    ElmCity.top_method = "auto";
    ElmCity.top_offset = 0;
    ElmCity.top_element = 'elmcity_sidebar_top';
    ElmCity.events_url = "";
    ElmCity.elmcity_id = "";
    ElmCity.host_dom_element = "";
    ElmCity.redirected_hubs = ['AnnArborChronicle'];
    ElmCity.current_event_id = "";
    return ElmCity;
})();

var Utils = (function () {
    function Utils() {
    }
    Utils.prototype.jQuerify = function (url, success) {
        var script = document.createElement('script');
        script.src = url;
        var head = document.getElementsByTagName('head')[0];
        var done = false;
        script.onload = script.onreadystatechange = function () {
            if (!done && (!this.readyState || this.readyState == 'loaded' || this.readyState == 'complete')) {
                done = true;
                success();
                script.onload = script.onreadystatechange = null;
                head.removeChild(script);
            }
        };
        head.appendChild(script);
    };

    Utils.apply_style_params = function () {
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
    };

    Utils.get_url_params = function () {
        if (Utils.gup('days') != '')
            ElmCityParams.days = Utils.gup('days');
        if (Utils.gup('eventsonly') != '')
            ElmCityParams.eventsonly = Utils.gup('eventsonly');
        if (Utils.gup('from') != '')
            ElmCityParams.from = Utils.gup('from');
        if (Utils.gup('jsurl') != '')
            ElmCityParams.jsurl = Utils.gup('jsurl');
        if (Utils.gup('host_dom_element') != '')
            ElmCityParams.host_dom_element = Utils.gup('host_dom_element');
        if (Utils.gup('mobile') != '')
            ElmCityParams.mobile = Utils.gup('mobile');
        if (Utils.gup('template') != '')
            ElmCityParams.template = Utils.gup('template');
        if (Utils.gup('tags') != '')
            ElmCityParams.template = Utils.gup('tags');
        if (Utils.gup('taglist') != '')
            ElmCityParams.template = Utils.gup('taglist');
        if (Utils.gup('theme') != '')
            ElmCityParams.theme = Utils.gup('theme');
        if (Utils.gup('timeofday') != '')
            ElmCityParams.theme = Utils.gup('timeofday');
        if (Utils.gup('to') != '')
            ElmCityParams.to = Utils.gup('to');
        if (Utils.gup('view') != '')
            ElmCityParams.view = Utils.gup('view');
    };

    Utils.process_params = function () {
        ElmCity.is_theme = ElmCityParams.theme != '';
        ElmCity.is_view = ElmCityParams.view != '';
        ElmCity.is_eventsonly = ElmCityParams.eventsonly.startsWith('y');
        ElmCity.is_mobile_declared = ElmCityParams.mobile.startsWith('y');
        ElmCity.is_mobile_detected = $j('#mobile_detected').text().trim() == "__MOBILE_DETECTED__";
        ElmCity.is_mobile = ElmCity.is_mobile_declared || ElmCity.is_mobile_detected;
        ElmCity.is_sidebar = (ElmCity.is_mobile == false) && (ElmCity.is_eventsonly == false);

        if (Utils.gup('tags').startsWith('n'))
            $j('.cat').remove();

        if (ElmCity.is_view && ElmCity.is_sidebar)
            try  {
                var href = $j('#subscribe').attr('href');
                href = href.replace('__VIEW__', ElmCityParams.view);
                $j('#subscribe').attr('href', href);
                $j('#subscribe').text('subscribe');
            } catch (e) {
                console.log(e.description);
            }

        if (ElmCityParams.timeofday.startsWith('n'))
            $j('.timeofday').remove();
    };

    Utils.parse_yyyy_mm_dd = function (date_str) {
        var match = /(\d{4,4})(\d{2,2})(\d{2,2})/.exec(date_str);
        return {
            year: match[1],
            month: Utils.maybe_zero_pad(match[2], 2),
            day: Utils.maybe_zero_pad(match[3], 2)
        };
    };

    Utils.parse_mm_dd_yyyy = function (date_str) {
        var match = /(\d{2,2})\/(\d{2,2})\/(\d{4,4})/.exec(date_str);
        return {
            month: Utils.maybe_zero_pad(match[1], 2),
            day: Utils.maybe_zero_pad(match[2], 2),
            year: match[3]
        };
    };

    Utils.scroll_to_element = function (id) {
        window.scrollTo(0, $j('#' + id).offset().top);
    };

    Utils.get_elmcity_id = function () {
        return $j('#elmcity_id').text().trim();
    };

    Utils.get_summary = function (id) {
        var elt = $j('#' + id);
        var summary = $j('#' + id + ' .ttl span').text();
        if (summary == '')
            summary = $j('#' + id + ' .ttl a').text();
        return summary;
    };

    Utils.get_dtstart = function (id) {
        return $j('#' + id + ' .st').attr('content');
    };

    Utils.maybe_zero_pad = function (str, len) {
        while (str.length < len)
            str = "0" + str;
        return str;
    };

    Utils.gup = function (name) {
        name = name.replace(/[\[]/, "\\\[").replace(/[\]]/, "\\\]");
        var regexS = "[\\?&]" + name + "=([^&#]*)";
        var regex = new RegExp(regexS);
        var results = regex.exec(window.location.href);
        if (results == null)
            return ""; else
            return results[1].replace(/%20/, ' ');
    };

    Utils.remove_href_arg = function (href, name) {
        var pat = eval('/[\?&]*' + name + '=[^&]*/');
        href = href.replace(pat, '');
        if ((!href.contains('?')) && href.contains('&'))
            href = href.replaceAt(href.indexOf('&'), '?');
        return href;
    };

    Utils.add_href_arg = function (href, name, value) {
        href = Utils.remove_href_arg(href, name);
        if (href.contains('?'))
            href = href + '&' + name + '=' + value; else {
            href = href + '?' + name + '=' + value;
        }
        return href;
    };

    Utils.propagate_internal_params = function (path) {
        for (var p in ElmCityParams) {
            if (p != undefined && ElmCityParams[p] != '')
                path = Utils.add_href_arg(path, p, ElmCityParams[p]);

            path = Utils.add_href_arg(path, 'view', ElmCityParams.view);
        }
        return path;
    };

    Utils.propagate_href_args = function (path) {
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
    };

    Utils.apply_json_css = function (jquery, element, style) {
        try  {
            var style = decodeURIComponent(Utils.gup(style));
            style = style.replace(/'/g, '"');
            jquery(element).css(JSON.parse(style));
        } catch (e) {
            console.log(e.message);
        }
    };

    Utils.remember_or_forget_days = function () {
        if (ElmCityParams.days != '0')
            Utils.remember_days(ElmCityParams.view, ElmCityParams.days); else
            Utils.forget_days(ElmCityParams.view);
    };

    Utils.remember_days = function (view, days) {
        try  {
            var cookie_name = Utils.make_cookie_name_from_view(view);
            $j.cookie(cookie_name, days);
        } catch (e) {
            console.log(e.message);
        }
    };

    Utils.forget_days = function (view) {
        try  {
            var cookie_name = Utils.make_cookie_name_from_view(view);
            $j.removeCookie(cookie_name);
        } catch (e) {
            console.log(e.message);
        }
    };

    Utils.make_cookie_name_from_view = function (view) {
        if (view == 'all')
            view = '';
        view = view.replace(',', '_');
        view = view.replace('-', '_minus_');
        var cookie_name = 'elmcity_' + view + '_days';
        return cookie_name;
    };

    Utils.get_add_to_cal_url = function (id, flavor) {
        var elt = $j('#' + id);
        var start = elt.find('.st').attr('content');
        var end = '';
        var url = elt.find('.ttl').find('a').attr('href');
        var summary = Utils.get_summary(id);
        var description = elt.find('.src').text();
        var location = '';

        var service_url = ElmCity.host + '/add_to_cal?elmcity_id=' + ElmCity.elmcity_id + '&flavor=' + flavor + '&start=' + encodeURIComponent(start) + '&end=' + end + '&summary=' + encodeURIComponent(summary) + '&url=' + encodeURIComponent(url) + '&description=' + encodeURIComponent(description) + '&location=' + location;
        return service_url;
    };

    Utils.add_to_google = function (id) {
        try  {
            var service_url = Utils.get_add_to_cal_url(id, 'google');
            $j('.menu').remove();

            //  console.log('redirecting to ' + service_url);
            //  location.href = service_url;
            window.open(service_url, "add to google");
        } catch (e) {
            console.log(e.message);
        }
    };

    Utils.add_to_hotmail = function (id) {
        var service_url = Utils.get_add_to_cal_url(id, 'hotmail');
        $j('.menu').remove();
        location.href = service_url;
    };

    Utils.add_to_ical = function (id) {
        var service_url = Utils.get_add_to_cal_url(id, 'ical');
        $j('.menu').remove();
        location.href = service_url;
    };

    Utils.add_to_facebook = function (id) {
        var service_url = Utils.get_add_to_cal_url(id, 'facebook');
        $j('.menu').remove();
        location.href = service_url;
    };

    Utils.dismiss_menu = function (id) {
        var elt = $j('#' + id);
        elt.find('.menu').remove();
    };

    Utils.active_description = function (description) {
        var quoted_id = '\'' + ElmCity.current_event_id + '\'';
        var x = '<span><a title="hide description" href="javascript:hide_desc(' + quoted_id + ')">[x]</a> </span>';
        var s = '<div style="overflow:hidden;text-indent:0" id="' + ElmCity.current_event_id + '_desc' + '">' + description + ' ' + x + '</div>';
        var elt = $j('#' + ElmCity.current_event_id);
        s = s.replace('<br><br>', '<br>');
        elt.append(s);
    };

    Utils.css_exists = function (uri) {
        var stylesheets = $j('link');
        for (var i = 0; i < stylesheets.length; i++)
            if (stylesheets[i].href == uri)
                return true;
        return false;
    };
    return Utils;
})();

// todo: consider encapsulating these in the ElmCity namespace when the generator provides qualified names
function add_to_cal(id) {
    var elt = $j('#' + id);
    var quoted_id = '\'' + id + '\'';
    elt.find('.menu').remove();
    elt.append('<ul class="menu">' + '<li><a title="add this event to your Google calendar" href="javascript:Utils.add_to_google(' + quoted_id + ')">add to Google Calendar</a></li>' + '<li><a title="add this event to your Hotmail calendar" href="javascript:Utils.add_to_hotmail(' + quoted_id + ')">add to Hotmail Calendar</a></li>' + '<li><a title="add to your Outlook, Apple iCal, or other iCalendar-aware desktop calendar" href="javascript:Utils.add_to_ical(' + quoted_id + ')">add to iCal</a></li>' + '<li><a title="add to Facebook (remind yourself and invite friends with 1 click!)" href="javascript:Utils.add_to_facebook(' + quoted_id + ')">add to Facebook</a></li>' + '<li><a title="dismiss this menu" href="javascript:Utils.dismiss_menu(' + quoted_id + ')">cancel</a></li>' + '</ul>');
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
    if (view == undefined) {
        var selected = $j('#tag_select option:selected').val();
        ElmCityParams.view = selected.replace(/\s*\((\d+)\)/, '');
        if (ElmCityParams.view == 'all')
            ElmCityParams.view = '';
    } else {
        ElmCityParams.view = view;
    }

    ElmCityParams.days = '0';

    try  {
        var days_cookie_name = ElmCity.make_cookie_name_from_view();
        var days_cookie_value = $j.cookie(days_cookie_name);
        if (days_cookie_value != undefined) {
            ElmCityParams.days = days_cookie_value;
        }
    } catch (e) {
        console.log(e.message);
    }

    ElmCity.events_url = Utils.propagate_internal_params(ElmCity.events_url);

    if (ElmCity.redirected_hubs.indexOf(ElmCity.elmcity_id) != -1)
        ElmCity.events_url = ElmCity.events_url.replace('/' + ElmCity.elmcity_id, '');

    if (ElmCity.injecting) {
        ElmCity.inject();
    } else {
        location.href = ElmCity.events_url;
    }
}

// remove this when all template dependencies are gone
function on_load() {
}

$j(document).ready(ElmCity.ready);
//@ sourceMappingURL=elmcity-2.js.map
