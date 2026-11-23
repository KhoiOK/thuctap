// ============================================================
//  User Management — site behaviour
//  Theme · sidebar · toasts · confirm modal · lightbox · form lock
//  · inline validation · responsive table
// ============================================================

(function () {
    'use strict';

    const $  = (sel, root = document) => root.querySelector(sel);
    const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

    const ICONS = {
        success: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="w-[1.125rem] h-[1.125rem]"><path d="M20 6 9 17l-5-5"/></svg>',
        error:   '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="w-[1.125rem] h-[1.125rem]"><circle cx="12" cy="12" r="10"/><path d="m15 9-6 6M9 9l6 6"/></svg>',
        warning: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="w-[1.125rem] h-[1.125rem]"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4M12 17h.01"/></svg>',
        info:    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="w-[1.125rem] h-[1.125rem]"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>'
    };

    const TITLES = {
        success: 'Thành công',
        error:   'Đã xảy ra lỗi',
        warning: 'Cảnh báo',
        info:    'Thông báo'
    };

    // ==========================================================
    //  Theme toggle
    // ==========================================================
    const Theme = (() => {
        const KEY = 'um-theme';
        const root = document.documentElement;

        const read = () => {
            try { return localStorage.getItem(KEY); } catch (e) { return null; }
        };
        const write = v => {
            try { localStorage.setItem(KEY, v); } catch (e) { /* noop */ }
        };
        const isDark = () => root.classList.contains('dark');

        function apply(dark) {
            root.classList.toggle('dark', dark);
            write(dark ? 'dark' : 'light');
            const btn = $('#themeToggle');
            if (btn) {
                btn.setAttribute('aria-pressed', String(dark));
                btn.setAttribute('title', dark ? 'Chuyển giao diện sáng' : 'Chuyển giao diện tối');
            }
        }

        function init() {
            apply(isDark());
            const btn = $('#themeToggle');
            if (!btn) return;
            btn.addEventListener('click', () => apply(!isDark()));
        }

        return { init, isDark, apply };
    })();

    // ==========================================================
    //  Sidebar (mobile drawer + desktop collapse)
    // ==========================================================
    const Sidebar = (() => {
        const sidebar = $('#sidebar');
        const backdrop = $('#sidebarBackdrop');
        const openBtn = $('#sidebarOpen');
        const closeBtn = $('#sidebarClose');
        const collapseBtn = $('#sidebarCollapse');
        const COLLAPSE_KEY = 'um-sidebar-collapsed';
        if (!sidebar) return { init() {} };

        // ---------- Mobile drawer ----------
        function setOpen(open) {
            sidebar.classList.toggle('is-open', open);
            backdrop && backdrop.classList.toggle('is-open', open);
            openBtn && openBtn.setAttribute('aria-expanded', String(open));
            document.body.style.overflow = open ? 'hidden' : '';
        }

        // ---------- Desktop collapse ----------
        function isCollapsed() {
            return document.body.classList.contains('sidebar-collapsed');
        }

        function setCollapsed(collapsed) {
            document.body.classList.toggle('sidebar-collapsed', collapsed);
            try { localStorage.setItem(COLLAPSE_KEY, collapsed ? '1' : '0'); } catch (e) { /* noop */ }
            if (collapseBtn) {
                collapseBtn.setAttribute('aria-expanded', String(!collapsed));
                collapseBtn.setAttribute('title', collapsed ? 'Mở rộng menu' : 'Thu gọn menu');
                collapseBtn.setAttribute('aria-label', collapsed ? 'Mở rộng menu' : 'Thu gọn menu');
            }
        }

        function init() {
            // Restore the persisted collapse state (desktop only).
            try {
                if (localStorage.getItem(COLLAPSE_KEY) === '1') setCollapsed(true);
            } catch (e) { /* noop */ }

            openBtn && openBtn.addEventListener('click', () => setOpen(true));
            closeBtn && closeBtn.addEventListener('click', () => setOpen(false));
            backdrop && backdrop.addEventListener('click', () => setOpen(false));
            document.addEventListener('keydown', e => {
                if (e.key === 'Escape' && sidebar.classList.contains('is-open')) setOpen(false);
            });
            // Close the drawer when a nav link is followed on mobile.
            $$('.sidebar-link[href]').forEach(a =>
                a.addEventListener('click', () => {
                    if (window.innerWidth < 1024) setOpen(false);
                }));
            // Reset inline scroll lock when resizing up to desktop.
            window.addEventListener('resize', () => {
                if (window.innerWidth >= 1024 && sidebar.classList.contains('is-open')) setOpen(false);
            });

            collapseBtn && collapseBtn.addEventListener('click', () => setCollapsed(!isCollapsed()));
        }

        return { init, close: () => setOpen(false) };
    })();

    // ==========================================================
    //  Toast
    // ==========================================================
    const Toast = (() => {
        function show(message, variant = 'info', durationMs = 4500) {
            const container = $('#toast-container');
            if (!container || !message) return;

            const v = ICONS[variant] ? variant : 'info';

            const wrapper = document.createElement('div');
            wrapper.className = `toast toast-${v}`;
            wrapper.setAttribute('role', variant === 'error' ? 'alert' : 'status');
            wrapper.innerHTML = `
                <span class="toast-icon">${ICONS[v]}</span>
                <div class="flex-1 min-w-0">
                    <p class="toast-title">${TITLES[v]}</p>
                    <p class="toast-text"></p>
                </div>
                <button type="button" class="icon-btn w-7 h-7 -mr-1 -mt-0.5 shrink-0" aria-label="Đóng">
                    <svg class="icon w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                         stroke-width="2.5" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>
                </button>`;
            // Safe: never innerHTML user/API text.
            wrapper.querySelector('.toast-text').textContent = message;

            const dismiss = () => {
                if (!wrapper.isConnected) return;
                wrapper.classList.add('is-leaving');
                setTimeout(() => wrapper.remove(), 300);
            };

            wrapper.querySelector('button').addEventListener('click', dismiss);
            container.appendChild(wrapper);
            setTimeout(dismiss, durationMs);
        }

        function init() {
            const auto = $('[data-toast-autoshow]');
            if (auto) {
                const text = auto.textContent.trim();
                if (text) show(text, auto.getAttribute('data-variant') || 'success');
                auto.remove();
            }
        }

        return { show, init };
    })();

    // ==========================================================
    //  Confirm-delete modal
    // ==========================================================
    const ConfirmDelete = (() => {
        const modal = $('#confirmDeleteModal');
        if (!modal) return { init() {}, open() {}, close() {} };

        const form = $('#confirmDeleteForm');
        const messageEl = $('[data-modal-message]', modal);
        const titleEl = $('#confirmDeleteTitle', modal);
        const submitBtn = $('[data-submit]', form);
        let lastFocused = null;

        function open(opts = {}) {
            messageEl.textContent = opts.message || 'Bạn có chắc chắn muốn xóa mục này?';
            if (titleEl) titleEl.textContent = opts.title || 'Xóa người dùng?';
            form.action = opts.action || form.action;

            $$('input[type="hidden"][data-extra]', form).forEach(e => e.remove());
            if (opts.returnUrl) {
                const hidden = document.createElement('input');
                hidden.type = 'hidden';
                hidden.name = 'returnUrl';
                hidden.value = opts.returnUrl;
                hidden.setAttribute('data-extra', '1');
                form.appendChild(hidden);
            }

            lastFocused = document.activeElement;
            modal.classList.add('is-open');
            modal.removeAttribute('aria-hidden');
            document.body.style.overflow = 'hidden';
            setTimeout(() => submitBtn && submitBtn.focus(), 60);
        }

        function close() {
            modal.classList.remove('is-open');
            modal.setAttribute('aria-hidden', 'true');
            document.body.style.overflow = '';
            if (lastFocused && lastFocused.focus) lastFocused.focus();
        }

        function init() {
            $$('[data-modal-close]', modal).forEach(b => b.addEventListener('click', close));
            modal.addEventListener('mousedown', e => { if (e.target === modal) close(); });
            document.addEventListener('keydown', e => {
                if (e.key === 'Escape' && modal.classList.contains('is-open')) close();
                // Trap focus inside the dialog.
                if (e.key === 'Tab' && modal.classList.contains('is-open')) {
                    const focusables = $$('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])', modal)
                        .filter(el => !el.disabled && el.offsetParent !== null);
                    if (!focusables.length) return;
                    const first = focusables[0];
                    const last = focusables[focusables.length - 1];
                    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
                    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
                }
            });

            document.addEventListener('click', e => {
                const trigger = e.target.closest('[data-confirm-delete]');
                if (!trigger) return;
                // Let the trigger handle it natively when JS is not wired (progressive enhancement).
                if (!form) return;
                e.preventDefault();
                open({
                    message: trigger.getAttribute('data-confirm-message')
                        || trigger.getAttribute('title')
                        || 'Bạn có chắc chắn muốn xóa mục này?',
                    title: trigger.getAttribute('data-confirm-title') || 'Xóa người dùng?',
                    action: trigger.getAttribute('href') || trigger.getAttribute('data-action'),
                    returnUrl: trigger.getAttribute('data-return-url')
                });
            });
        }

        return { init, open, close };
    })();

    // ==========================================================
    //  Form submit lock (prevent double submits)
    // ==========================================================
    const FormLock = (() => {
        function init() {
            document.addEventListener('submit', e => {
                const form = e.target;
                if (!(form instanceof HTMLFormElement)) return;
                if (form.dataset.skipLock === '1') return;

                const submitter = e.submitter;
                if (submitter && submitter.dataset && submitter.dataset.skipLock === '1') return;

                $$('button[type="submit"]', form).forEach(btn => {
                    if (btn.disabled) return;
                    btn.disabled = true;
                    btn.dataset.originalHtml = btn.innerHTML;
                    btn.innerHTML = `
                        <svg class="icon animate-spin" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                             stroke-width="2.5" stroke-linecap="round">
                          <path d="M21 12a9 9 0 1 1-6.219-8.56" stroke-linecap="round" stroke-linejoin="round"/>
                        </svg>
                        <span>Đang xử lý…</span>`;
                });
            });
        }
        return { init };
    })();

    // ==========================================================
    //  Async delete (POST -> API, no full page reload)
    // ==========================================================
    const AsyncDelete = (() => {
        const restore = btn => {
            if (!btn) return;
            btn.disabled = false;
            btn.textContent = 'Xóa';
        };

        function init() {
            document.addEventListener('submit', async e => {
                const form = e.target;
                if (!(form instanceof HTMLFormElement)) return;
                if (!form.hasAttribute('data-async-delete')) return;
                e.preventDefault();

                const submitBtn = $('[data-submit]', form);
                if (submitBtn) {
                    submitBtn.disabled = true;
                    submitBtn.textContent = 'Đang xóa…';
                }

                const token = $('input[name="__RequestVerificationToken"]', form);
                const headers = { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' };
                if (token) headers['RequestVerificationToken'] = token.value;

                try {
                    const res = await fetch(form.action, {
                        method: 'POST',
                        headers,
                        credentials: 'same-origin',
                        body: new FormData(form)
                    });

                    if (res.ok || res.status === 204) {
                        Toast.show('Mục đã được xóa thành công.', 'success');
                        const redirect = form.getAttribute('data-success-redirect')
                            || (form.querySelector('input[name="returnUrl"]') || {}).value;
                        setTimeout(() => {
                            window.location.href = redirect || window.location.href;
                        }, 400);
                        return;
                    }

                    let detail = 'Không thể xóa mục này.';
                    try {
                        const body = await res.json();
                        detail = body.detail || body.title || body.error || detail;
                    } catch (e) { /* non-JSON error body */ }

                    if (res.status === 401 || res.status === 403) detail = 'Bạn không có quyền thực hiện thao tác này.';
                    else if (res.status === 409) detail = 'Dữ liệu đang được sử dụng, không thể xóa.';

                    Toast.show(detail, 'error');
                    restore(submitBtn);
                    ConfirmDelete.close();
                } catch (err) {
                    Toast.show('Lỗi mạng: ' + err.message, 'error');
                    restore(submitBtn);
                    ConfirmDelete.close();
                }
            });
        }
        return { init };
    })();

    // ==========================================================
    //  Lightbox
    // ==========================================================
    const Lightbox = (() => {
        let overlay = null;
        let lastFocused = null;

        function hide() {
            if (!overlay) return;
            overlay.remove();
            overlay = null;
            document.body.style.overflow = '';
            if (lastFocused && lastFocused.focus) lastFocused.focus();
        }

        function show(src, alt) {
            if (!src) return;
            hide();

            lastFocused = document.activeElement;
            overlay = document.createElement('div');
            overlay.className = 'lightbox-overlay';
            overlay.setAttribute('role', 'dialog');
            overlay.setAttribute('aria-modal', 'true');
            overlay.setAttribute('aria-label', alt || 'Xem ảnh');

            const img = document.createElement('img');
            img.src = src;
            img.alt = alt || '';
            img.className = 'lightbox-img';

            const close = document.createElement('button');
            close.type = 'button';
            close.className = 'lightbox-close';
            close.setAttribute('aria-label', 'Đóng');
            close.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" class="w-5 h-5"><path d="M18 6 6 18M6 6l12 12"/></svg>';

            overlay.appendChild(img);
            overlay.appendChild(close);
            overlay.addEventListener('click', hide);
            document.body.appendChild(overlay);
            document.body.style.overflow = '';
            close.focus();
        }

        function init() {
            document.addEventListener('click', e => {
                const trigger = e.target.closest('[data-lightbox]');
                if (!trigger) return;
                e.preventDefault();
                show(trigger.getAttribute('data-lightbox') || trigger.getAttribute('href') || trigger.src,
                     trigger.getAttribute('data-lightbox-alt') || trigger.alt || '');
            });
            document.addEventListener('keydown', e => {
                if (e.key === 'Escape' && overlay) hide();
            });
        }
        return { init, show, hide };
    })();

    // ==========================================================
    //  Inline validation styling
    //  ------------------------------------------------------------
    //  Two layers cooperate here:
    //   1. jquery-validation unobtrusive (loaded on the form pages)
    //      renders the message text into the `[data-valmsg-for]` span
    //      that `asp-validation-for` produces, and toggles the
    //      `input-validation-error` class on the input.
    //   2. This module adds the visual `field-invalid` ring so the
    //      input matches the error message, and - only when the
    //      unobtrusive adapter is NOT present (e.g. a form without
    //      the validation scripts) - supplies its own message slot.
    // ==========================================================
    const FieldValidation = (() => {
        const MESSAGES = {
            valueMissing:    'Trường này là bắt buộc.',
            typeMismatch:    'Giá trị không đúng định dạng.',
            patternMismatch: 'Giá trị không đúng định dạng.',
            rangeUnderflow:  'Giá trị quá nhỏ.',
            rangeOverflow:   'Giá trị quá lớn.',
            tooShort:        'Giá trị quá ngắn.',
            tooLong:         'Giá trị quá dài.',
            stepMismatch:    'Giá trị không hợp lệ.',
            badInput:        'Giá trị không hợp lệ.'
        };

        // Map HTML5 ValidityState flags to a human-readable message.
        function validityMessage(input) {
            const v = input.validity;
            if (v.valueMissing)   return MESSAGES.valueMissing;
            if (v.typeMismatch)   return MESSAGES.typeMismatch;
            if (v.patternMismatch) return MESSAGES.patternMismatch;
            if (v.rangeUnderflow) return MESSAGES.rangeUnderflow;
            if (v.rangeOverflow)  return MESSAGES.rangeOverflow;
            if (v.tooShort)       return MESSAGES.tooShort;
            if (v.tooLong)        return MESSAGES.tooLong;
            if (v.stepMismatch)   return MESSAGES.stepMismatch;
            if (v.badInput)       return MESSAGES.badInput;
            return null;
        }

        // The container rendered by `asp-validation-for`. The unobtrusive
        // adapter looks it up by the input's *name* attribute.
        function messageContainer(input) {
            const name = input.getAttribute('name');
            if (!name) return null;
            return document.querySelector(`[data-valmsg-for="${CSS.escape(name)}"]`);
        }

        // True when the unobtrusive adapter has attached to this form.
        function hasUnobtrusive(input) {
            const form = input.form;
            if (!form) return false;
            const $form = window.jQuery ? window.jQuery(form) : null;
            if (!$form) return false;
            return !!$form.data('validator');
        }

        // Sync the visual invalid state from the library's own error class.
        function syncFromLibrary(input) {
            const invalid = input.classList.contains('input-validation-error');
            input.classList.toggle('field-invalid', invalid);
        }

        // Fallback used when the unobtrusive adapter is not loaded.
        function refreshStandalone(input) {
            const hasValue = input.value.trim() !== '';
            const invalid = hasValue && !input.checkValidity();
            input.classList.toggle('field-invalid', invalid);

            const container = messageContainer(input);
            if (!container) return;

            if (invalid) {
                container.textContent = validityMessage(input) || 'Giá trị không hợp lệ.';
                container.classList.remove('hidden');
            } else {
                container.textContent = '';
                container.classList.add('hidden');
            }
        }

        function refresh(input) {
            if (hasUnobtrusive(input)) syncFromLibrary(input);
            else refreshStandalone(input);
        }

        function init() {
            $$('.field').forEach(input => {
                if (input.type === 'hidden' || input.type === 'file') return;

                input.addEventListener('blur', () => refresh(input));
                input.addEventListener('input', () => {
                    if (input.classList.contains('field-invalid')) refresh(input);
                });
                refresh(input);
            });

            // Keep the visual ring in sync with the unobtrusive adapter,
            // which adds/removes `input-validation-error` on validation.
            const $ = window.jQuery;
            if ($) {
                $$('form').forEach(form => {
                    const $form = $(form);
                    if (!$form.data('validator')) return;
                    $form.on('invalid.field', e => {
                        const el = e.target;
                        if (el && el.classList) syncFromLibrary(el);
                    });
                    $form.on('valid.field', e => {
                        const el = e.target;
                        if (el && el.classList) syncFromLibrary(el);
                    });
                });
            }
        }
        return { init };
    })();

    // ==========================================================
    //  Debounce helper (exposed for views, e.g. live search)
    // ==========================================================
    function debounce(fn, wait = 350) {
        let t;
        return function (...args) {
            clearTimeout(t);
            t = setTimeout(() => fn.apply(this, args), wait);
        };
    }

    // ==========================================================
    //  Boot
    // ==========================================================
    function boot() {
        Theme.init();
        Sidebar.init();
        Toast.init();
        ConfirmDelete.init();
        FormLock.init();
        AsyncDelete.init();
        Lightbox.init();
        FieldValidation.init();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }

    // Public API
    window.Toast = Toast;
    window.ConfirmDelete = ConfirmDelete;
    window.Lightbox = Lightbox;
    window.Theme = Theme;
    window.debounce = debounce;
})();
