/* =============================================================================
   SCR-07 – Quét tự động thông tin giấy tờ (OCR)
   -----------------------------------------------------------------------------
   - Nút "Quét tự động" chỉ active khi đủ ảnh mặt trước + mặt sau.
   - Gọi API-12, sau đó điền sẵn các trường đạt ngưỡng tin cậy (BR-14).
   - Trường có confidence thấp KHÔNG được điền tự động.
   - Trường OCR vi phạm validation vẫn được điền kèm lỗi tương ứng.
   - Không tự động submit – người dùng phải bấm "Lưu".
   ========================================================================== */
(function () {
    'use strict';

    const scanBtn = document.querySelector('[data-ocr-scan]');
    const skipBtn = document.querySelector('[data-ocr-skip]');
    const panelHost = document.querySelector('[data-ocr-panel-host]');
    const typeSelect = document.querySelector('[data-doc-type]');

    if (!scanBtn || !panelHost) return;

    const frontInput = document.querySelector('input[name="FrontImage"]');
    const backInput = document.querySelector('input[name="BackImage"]');
    const backSection = document.querySelector('[data-image-section="back"]');

    const label = scanBtn.querySelector('[data-ocr-scan-label]');
    const originalLabel = label ? label.textContent.trim() : 'Quét tự động';

    // Ngưỡng tin cậy (phải khớp với Ocr:MinConfidence / Ocr:HighConfidence).
    const MIN_CONFIDENCE = 0.60;
    const HIGH_CONFIDENCE = 0.85;

    // Trường form <- trường OCR.
    const FIELD_MAP = {
        documentNumber: 'DocumentNumber',
        issuedDate: 'IssuedDate',
        expiryDate: 'ExpiryDate'
    };

    let scanId = null;
    let scanning = false;

    // Hidden field that carries the scan id to the server. When it is filled, the form
    // is submitted through API-13 (OCR confirm) so the temporary images are promoted
    // to their permanent folder instead of being uploaded a second time.
    const scanIdInput = document.querySelector('[data-ocr-scan-id]');

    function setScanId(value) {
        scanId = value || null;
        if (scanIdInput) scanIdInput.value = scanId || '';
    }

    function clearScanId() {
        setScanId(null);
        panelHost.innerHTML = '';
        if (skipBtn) skipBtn.classList.add('hidden');
        if (label) label.textContent = originalLabel;
        document.querySelectorAll('[data-ocr-badge]').forEach(function (b) {
            b.innerHTML = '';
            b.classList.add('hidden');
        });
    }

    function isPassport() {
        const type = typeSelect ? typeSelect.value : 'CCCD';
        return type.toLowerCase() === 'passport';
    }

    function hasFile(input) {
        return !!(input && input.files && input.files.length > 0 && input.files[0].size > 0);
    }

    function updateScanButton() {
        if (scanning) return;

        const needBack = !isPassport();
        const ready = hasFile(frontInput) && (!needBack || hasFile(backInput));

        scanBtn.disabled = !ready;
        scanBtn.title = ready
            ? 'Quét tự động thông tin từ ảnh đã chọn'
            : 'Vui lòng chọn đủ ảnh mặt trước và mặt sau';
    }

    function setScanning(state) {
        scanning = state;
        scanBtn.disabled = state;
        if (label) {
            label.textContent = state ? 'Đang quét…' : (scanId ? 'Quét lại' : originalLabel);
        }
    }

    function badgeHtml(kind) {
        const icons = {
            high: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            medium: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 9v4M12 17h.01"/><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/></svg>',
            low: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M15 9l-6 6M9 9l6 6"/></svg>',
            missing: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>'
        };
        const icon = icons[kind] || icons.missing;
        const text = kind === 'high' ? 'Tin cậy cao'
            : kind === 'medium' ? 'Cần kiểm tra'
                : kind === 'low' ? 'Độ tin cậy thấp'
                    : 'Không đọc được';
        return icon + '<span>' + text + '</span>';
    }

    function kindOf(field) {
        if (!field) return null;
        if (!field.value || !String(field.value).trim()) return 'missing';
        if (field.confidence >= HIGH_CONFIDENCE) return 'high';
        if (field.confidence >= MIN_CONFIDENCE) return 'medium';
        return 'low';
    }

    function applyFieldValue(formFieldName, value) {
        const input = document.querySelector('[name="' + formFieldName + '"]');
        if (!input) return;
        input.value = value;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function setInlineBadge(ocrFieldName, kind) {
        const holder = document.querySelector('[data-ocr-badge="' + ocrFieldName + '"]');
        if (!holder) return;
        if (!kind) {
            holder.innerHTML = '';
            holder.classList.add('hidden');
            return;
        }
        holder.innerHTML = badgeHtml(kind);
        holder.className = 'ocr-inline-badge ocr-inline-' + kind;
        holder.title = 'Nguồn: quét tự động';
    }

    function renderPanel(data) {
        const fields = data.fields || {};
        const names = {
            documentNumber: 'Số giấy tờ',
            fullName: 'Họ và tên',
            dateOfBirth: 'Ngày sinh',
            gender: 'Giới tính',
            address: 'Địa chỉ',
            issuedDate: 'Ngày cấp',
            expiryDate: 'Ngày hết hạn',
            authorityNo: 'Số hiệu'
        };

        const items = Object.keys(fields).map(function (key) {
            const f = fields[key] || {};
            const kind = kindOf(f);
            const pct = f.value ? Math.round((f.confidence || 0) * 100) + '%' : '—';
            const display = (f.value && String(f.value).trim()) ? f.value : 'Không đọc được';
            const emptyClass = (f.value && String(f.value).trim()) ? '' : ' is-empty';
            return '<li class="ocr-field-item">' +
                '<span class="ocr-field-name">' + (names[key] || key) + '</span>' +
                '<span class="ocr-field-value' + emptyClass + '">' + escapeHtml(display) + '</span>' +
                '<span class="ocr-badge ocr-badge-' + (kind || 'none') + '" title="Độ tin cậy ' + pct + '">' +
                badgeHtml(kind) + '</span>' +
                '</li>';
        }).join('');

        const warnings = data.warnings || [];
        const warningHtml = warnings.length
            ? '<div class="ocr-warnings" role="status"><p class="ocr-warnings-title">Cần kiểm tra</p><ul class="ocr-warnings-list">' +
            warnings.map(function (w) { return '<li>' + escapeHtml(w) + '</li>'; }).join('') +
            '</ul></div>'
            : '';

        panelHost.innerHTML =
            '<div class="ocr-panel" data-ocr-panel>' +
            '<div class="ocr-panel-head">' +
            '<span class="ocr-panel-icon" aria-hidden="true">' +
            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
            '<path d="M3 7V5a2 2 0 0 1 2-2h2M17 3h2a2 2 0 0 1 2 2v2M21 17v2a2 2 0 0 1-2 2h-2M7 21H5a2 2 0 0 1-2-2v-2"/><path d="M7 8h10M7 12h6"/>' +
            '</svg></span>' +
            '<div class="min-w-0 flex-1">' +
            '<h3 class="ocr-panel-title">Kết quả quét tự động</h3>' +
            '<p class="ocr-panel-sub">Đã đọc được thông tin từ ảnh. Vui lòng kiểm tra lại trước khi lưu.</p>' +
            '</div>' +
            '<span class="ocr-panel-time">' + (data.processingTimeMs || 0) + ' ms</span>' +
            '</div>' +
            '<ul class="ocr-field-list">' + items + '</ul>' +
            warningHtml +
            '</div>';

        // Điền sẵn các trường đạt ngưỡng tin cậy (BR-14).
        Object.keys(FIELD_MAP).forEach(function (ocrField) {
            const f = fields[ocrField];
            const kind = kindOf(f);
            setInlineBadge(ocrField, kind);

            if (!f) return;
            const hasValue = !!(f.value && String(f.value).trim());

            if (!hasValue) return;

            if (!f.isValid) {
                // Giá trị sai định dạng: vẫn điền để người dùng sửa, kèm cảnh báo.
                applyFieldValue(FIELD_MAP[ocrField], f.value);
                return;
            }

            if (f.confidence < MIN_CONFIDENCE) {
                // Không đủ tin cậy: không điền tự động.
                return;
            }

            applyFieldValue(FIELD_MAP[ocrField], f.value);
        });

        // Badge cho các trường không nằm trong form (chỉ hiển thị trong panel).
        ['fullName', 'dateOfBirth', 'gender', 'address', 'authorityNo'].forEach(function (name) {
            setInlineBadge(name, null);
        });
    }

    function escapeHtml(value) {
        const div = document.createElement('div');
        div.textContent = value == null ? '' : String(value);
        return div.innerHTML;
    }

    function renderEmptyPanel(message) {
        panelHost.innerHTML =
            '<div class="ocr-panel ocr-panel-empty" role="status">' +
            '<p class="ocr-panel-sub">' + escapeHtml(message) + '</p>' +
            '</div>';
    }

    async function runScan() {
        if (scanning) return;

        const form = scanBtn.closest('form');
        if (!form) return;

        const data = new FormData();
        data.append('documentType', typeSelect ? typeSelect.value : 'CCCD');

        if (frontInput && frontInput.files && frontInput.files[0]) {
            data.append('frontImage', frontInput.files[0], frontInput.files[0].name);
        }
        if (backInput && backInput.files && backInput.files[0]) {
            data.append('backImage', backInput.files[0], backInput.files[0].name);
        }

        setScanning(true);

        try {
            // Gọi MVC endpoint (proxy sang API-12) để giữ anti-forgery token.
            const response = await fetch(scanUrl(), {
                method: 'POST',
                body: data,
                headers: { 'RequestVerificationToken': getAntiForgeryToken() },
                credentials: 'same-origin'
            });

            const payload = await response.json().catch(function () { return null; });

            if (response.ok && payload && payload.ok) {
                setScanId(payload.data.scanId);
                renderPanel(payload.data);
                if (skipBtn) skipBtn.classList.remove('hidden');
                Toast.show('Đã quét xong. Vui lòng kiểm tra thông tin trước khi lưu.', 'success');
            } else {
                clearScanId();
                const message = (payload && payload.message)
                    ? payload.message
                    : 'Không đọc được thông tin trên ảnh. Bạn vẫn có thể nhập thủ công.';

                renderEmptyPanel(message);
                Toast.show(message, 'error');
            }
        } catch (err) {
            clearScanId();
            renderEmptyPanel('Không thể kết nối tới máy chủ. Bạn vẫn có thể nhập thủ công.');
            Toast.show('Không thể kết nối tới máy chủ. Vui lòng thử lại.', 'error');
        } finally {
            setScanning(false);
            updateScanButton();
        }
    }

    function scanUrl() {
        const holder = document.querySelector('[data-ocr-scan-url]');
        if (holder) return holder.dataset.ocrScanUrl;
        return formAction() + '/ocr/scan';
    }

    function formAction() {
        const form = scanBtn.closest('form');
        return form ? form.getAttribute('action') : '';
    }

    function getAntiForgeryToken() {
        const input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    scanBtn.addEventListener('click', runScan);

    if (skipBtn) {
        skipBtn.addEventListener('click', function () {
            clearScanId();
        });
    }

    document.querySelectorAll('input[type="file"]').forEach(function (input) {
        input.addEventListener('change', function () {
            // Đổi ảnh thì kết quả quét cũ không còn đúng.
            if (scanId) {
                clearScanId();
            }
            updateScanButton();
        });
    });

    if (typeSelect) {
        typeSelect.addEventListener('change', updateScanButton);
    }

    updateScanButton();
})();
