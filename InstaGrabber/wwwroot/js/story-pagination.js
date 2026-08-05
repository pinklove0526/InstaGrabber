// Client-side paging for the results view.
//
// Every parsed item is already in the DOM — the server renders the whole reel and nothing
// here talks to it. This only toggles `display` on the <li> elements, so the badges,
// captions and download links inside each card are untouched: an item on a hidden page has
// exactly the markup it had before, and its download link works the moment it is shown
// again. With JS off, every item stays visible and no pager appears.
(function () {
    'use strict';

    var PAGE_SIZE = 6;

    /// Only the reel's own cards. A carousel item nests another <ul class="thumbnails">
    /// inside its card, and those children must never be counted or hidden separately.
    function cardsOf(list) {
        var cards = [];
        for (var i = 0; i < list.children.length; i++) {
            if (list.children[i].tagName === 'LI') {
                cards.push(list.children[i]);
            }
        }
        return cards;
    }

    function link(label, ariaLabel) {
        var a = document.createElement('a');
        a.href = '#';
        a.innerHTML = label;
        if (ariaLabel) {
            a.setAttribute('aria-label', ariaLabel);
        }
        return a;
    }

    // Bootstrap 2's pagination is a <div class="pagination"> wrapping a plain <ul>; the
    // `disabled` and `active` classes live on the <li>, not the <a>.
    function buildPager(pageCount, onSelect) {
        var nav = document.createElement('div');
        nav.className = 'pagination pagination-centered';
        nav.setAttribute('role', 'navigation');
        nav.setAttribute('aria-label', 'Media pages');

        var list = document.createElement('ul');
        var entries = [];

        function entry(label, target, ariaLabel) {
            var li = document.createElement('li');
            var anchor = link(label, ariaLabel);
            li.appendChild(anchor);
            list.appendChild(li);
            anchor.onclick = function (event) {
                event.preventDefault();
                if (li.className.indexOf('disabled') === -1) {
                    onSelect(target());
                }
            };
            return li;
        }

        var current = 1;
        var previous = entry('&laquo;', function () { return current - 1; }, 'Previous page');
        for (var page = 1; page <= pageCount; page++) {
            entries.push(entry(String(page), (function (n) {
                return function () { return n; };
            })(page), 'Page ' + page));
        }
        var next = entry('&raquo;', function () { return current + 1; }, 'Next page');

        nav.appendChild(list);

        return {
            element: nav,
            // Repaints the control to match the page the list is showing.
            sync: function (page) {
                current = page;
                previous.className = page === 1 ? 'disabled' : '';
                next.className = page === pageCount ? 'disabled' : '';
                for (var i = 0; i < entries.length; i++) {
                    entries[i].className = i + 1 === page ? 'active' : '';
                }
            }
        };
    }

    function paginate(list) {
        var cards = cardsOf(list);

        // A handful of results needs no pager, so none is rendered at all.
        if (cards.length <= PAGE_SIZE) {
            return;
        }

        var pageCount = Math.ceil(cards.length / PAGE_SIZE);
        var pager = buildPager(pageCount, function (page) {
            show(page);
            // Landing mid-list after a page change is disorienting. Guarded so a missing
            // implementation costs the scroll, not the paging.
            if (list.scrollIntoView) {
                list.scrollIntoView();
            }
        });

        function show(page) {
            for (var i = 0; i < cards.length; i++) {
                cards[i].style.display = Math.floor(i / PAGE_SIZE) + 1 === page ? '' : 'none';
            }
            pager.sync(page);
        }

        list.parentNode.insertBefore(pager.element, list.nextSibling);
        show(1);
    }

    function start() {
        var lists = document.querySelectorAll('[data-ig-paginate]');
        for (var i = 0; i < lists.length; i++) {
            paginate(lists[i]);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
