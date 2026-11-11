// Rotorcraft website: header, menu, trailer, hangar tabs, screenshot viewer, reveal on scroll. No libraries.
(function () {
  var top = document.getElementById('top');
  var onScroll = function () { top.classList.toggle('solid', window.scrollY > 40); };
  window.addEventListener('scroll', onScroll, { passive: true }); onScroll();

  // menu on narrow screens
  var nav = document.getElementById('nav'), menu = document.getElementById('menu-btn');
  menu.addEventListener('click', function () {
    var open = nav.classList.toggle('open');
    menu.setAttribute('aria-expanded', open ? 'true' : 'false');
  });
  nav.addEventListener('click', function (e) {
    if (e.target.closest('a')) { nav.classList.remove('open'); menu.setAttribute('aria-expanded', 'false'); }
  });

  // the section in view is marked in the menu
  var links = Array.prototype.slice.call(nav.querySelectorAll('a'));
  if ('IntersectionObserver' in window) {
    var spy = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (!en.isIntersecting) return;
        var id = en.target.id === 'jobs' ? 'features' : en.target.id;   // the jobs belong with the features
        links.forEach(function (a) { a.classList.toggle('on', a.getAttribute('href') === '#' + id); });
      });
    }, { rootMargin: '-45% 0px -50% 0px' });
    links.forEach(function (a) { var s = document.querySelector(a.getAttribute('href')); if (s) spy.observe(s); });
    spy.observe(document.getElementById('jobs'));
  }

  // trailer: the poster button starts the video, which loads only then
  var video = document.getElementById('video'), play = document.getElementById('play');
  play.addEventListener('click', function () {
    play.remove();
    var p = video.play(); if (p && p.catch) p.catch(function () {});
    video.focus();
  });

  // hangar tabs (arrow keys move between them)
  var tabs = Array.prototype.slice.call(document.querySelectorAll('.ac-tab'));
  var choose = function (tab, focus) {
    tabs.forEach(function (t) {
      var on = t === tab;
      t.setAttribute('aria-selected', on ? 'true' : 'false');
      t.tabIndex = on ? 0 : -1;
      document.getElementById(t.getAttribute('aria-controls')).hidden = !on;
    });
    if (focus) tab.focus();
    tab.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  };
  tabs.forEach(function (t, i) {
    t.addEventListener('click', function () { choose(t, false); });
    t.addEventListener('keydown', function (e) {
      var k = e.key, n = tabs.length;
      if (k === 'ArrowDown' || k === 'ArrowRight') { e.preventDefault(); choose(tabs[(i + 1) % n], true); }
      if (k === 'ArrowUp' || k === 'ArrowLeft') { e.preventDefault(); choose(tabs[(i + n - 1) % n], true); }
      if (k === 'Home') { e.preventDefault(); choose(tabs[0], true); }
      if (k === 'End') { e.preventDefault(); choose(tabs[n - 1], true); }
    });
  });

  // screenshot viewer
  var shots = Array.prototype.slice.call(document.querySelectorAll('.shot'));
  var box = document.getElementById('lightbox'), img = document.getElementById('lb-img'), cap = document.getElementById('lb-cap');
  var at = 0;
  var show = function (i) {
    at = (i + shots.length) % shots.length;
    var thumb = shots[at].querySelector('img');
    img.src = shots[at].getAttribute('data-full');
    img.alt = thumb.alt;
    cap.textContent = thumb.alt + '  ·  ' + (at + 1) + ' of ' + shots.length;
  };
  if (typeof box.showModal === 'function') {
    shots.forEach(function (s, i) { s.addEventListener('click', function () { show(i); box.showModal(); }); });
    document.getElementById('lb-close').addEventListener('click', function () { box.close(); });
    document.getElementById('lb-prev').addEventListener('click', function () { show(at - 1); });
    document.getElementById('lb-next').addEventListener('click', function () { show(at + 1); });
    box.addEventListener('click', function (e) { if (e.target === box || e.target.tagName === 'FIGURE') box.close(); });
    box.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowLeft') show(at - 1);
      if (e.key === 'ArrowRight') show(at + 1);
    });
    var x0 = null;
    box.addEventListener('touchstart', function (e) { x0 = e.touches[0].clientX; }, { passive: true });
    box.addEventListener('touchend', function (e) {
      if (x0 === null) return;
      var dx = e.changedTouches[0].clientX - x0; x0 = null;
      if (Math.abs(dx) > 50) show(at + (dx < 0 ? 1 : -1));
    });
    box.addEventListener('close', function () { img.removeAttribute('src'); });
  } else {
    // no <dialog>: open the full picture on its own
    shots.forEach(function (s) { s.addEventListener('click', function () { window.open(s.getAttribute('data-full'), '_blank'); }); });
  }

  // cards rise into place as they come into view
  var rise = document.querySelectorAll('.rise');
  if ('IntersectionObserver' in window && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) { if (en.isIntersecting) { en.target.classList.add('in'); io.unobserve(en.target); } });
    }, { rootMargin: '0px 0px -8% 0px' });
    rise.forEach(function (el) { io.observe(el); });
  } else {
    rise.forEach(function (el) { el.classList.add('in'); });
  }
})();
