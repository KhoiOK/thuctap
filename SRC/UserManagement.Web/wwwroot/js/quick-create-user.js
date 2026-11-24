/* =============================================================================
   Quick Create — "Thêm User nhanh"
   -----------------------------------------------------------------------------
   - Two input modes: manual entry and CCCD scanning (OCR preview).
   - The OCR preview (API-12b via the MVC proxy) only PRE-FILLS the form:
     nothing is persisted until the user presses "Tạo User" (business rule 2/3).
   - Fields with a low confidence are NOT auto-filled (BR-14) but are still shown
     in the result panel so the user can check them.
   - The submit button is locked while the request is in flight (business rule 5).
   ========================================================================== */
(function () {
    'use strict';

    const form = document.querySelector('[data-quick-create]');
    if (!form) return;

    const modeInput = form.querySelector('[data-quick-mode]');
    const modeCards = Array.from(form.querySelectorAll('[data-mode-option]'));
    const ocrBranch = form.querySelector('[data-ocr-branch]');
    const ocrDocumentFields = form.querySelector('[data-ocr-document-fields]');
    const previewBtn = form.querySelector('[data-ocr-preview-btn]');
    const previewLabel = form.querySelector('[data-ocr-preview-label]');
    const previewUrl = form.dataset.ocrPreviewUrl;
    const panelHost = form.querySelector('[data-ocr-panel-host]');
    const statusEl = form.querySelector('[data-ocr-status]');
    const docTypeSelect = form.querySelector('[data-doc-type]');
    const backSection = form.querySelector('[data-ocr-back-section]');
    const submitBtn = form.querySelector('[data-quick-submit]');

    const frontInput = form.querySelector('[data-ocr-image="front"]');
    const backInput = form.querySelector('[data-ocr-image="back"]');

    // Ngưỡng tin cậy (phải khớp với Ocr:MinConfidence / Ocr:HighConfidence).
    const MIN_CONFIDENCE = 0.60;
    const HIGH_CONFIDENCE = 0.85;

    // Trường form <- trường OCR. Giá trị được ghi vào ô tương ứng.
    const FIELD_MAP = {
        fullName: 'FullName',
        dateOfBirth: 'DateOfBirth',
        gender: 'Gender',
        address: 'Address',
        documentNumber: 'DocumentNumber',
        issuedDate: 'IssuedDate',
        expiryDate: 'ExpiryDate'
    };

    // Nhãn hiển thị cho các trường OCR trong bảng kết quả.
    const FIELD_LABELS = {
        documentNumber: 'Số giấy tờ',
        fullName: 'Họ và tên',
        dateOfBirth: 'Ngày sinh',
        gender: 'Giới tính',
        address: 'Địa chỉ',
        issuedDate: 'Ngày cấp',
        expiryDate: 'Ngày hết hạn',
        authorityNo: 'Số hiệu'
    };

    // Chuẩn hóa giá trị OCR về định dạng mà <input type="date"> chấp nhận.
    const DATE_FIELDS = ['dateOfBirth', 'issuedDate', 'expiryDate'];

    let mode = (modeInput && modeInput.value) ? modeInput.value : 'manual';
    let scanning = false;

    // =================== helpers ===================

    function escapeHtml(value) {
        const div = document.createElement('div');
        div.textContent = value == null ? '' : String(value);
        return div.innerHTML;
    }

    function setMode(next) {
        mode = next;
        if (modeInput) modeInput.value = mode;

        modeCards.forEach(function (card) {
            const active = card.dataset.modeOption === mode;
            card.setAttribute('aria-checked', active ? 'true' : 'false');
            card.setAttribute('aria-pressed', active ? 'true' : 'false');
            card.classList.toggle('is-active', active);
            const state = card.querySelector('[data-mode-state]');
            if (state) {
                state.textContent = active ? 'Đang chọn' : 'Chọn phương thức';
            }
        });

        const isOcr = mode === 'ocr';
        if (ocrBranch) ocrBranch.hidden = !isOcr;
        if (ocrDocumentFields) ocrDocumentFields.hidden = !isOcr;

        // Khi rời khỏi nhánh OCR thì bỏ ảnh đã chọn: ảnh không thuộc về luồng này.
        if (!isOcr) {
            clearImages();
        }
        updatePreviewButton();
    }

    function isPassport() {
        const type = docTypeSelect ? docTypeSelect.value : 'CCCD';
        return type.toLowerCase() === 'passport';
    }

    function hasFile(input) {
        return !!(input && input.files && input.files.length > 0 && input.files[0].size > 0);
    }

    function clearImages() {
        [frontInput, backInput].forEach(function (input) {
            if (!input) return;
            input.value = '';
            const preview = form.querySelector('[data-preview-for="' + input.name + '"]');
            if (preview) {
                preview.classList.add('hidden');
                if (preview.dataset.objectUrl) {
                    URL.revokeObjectURL(preview.dataset.objectUrl);
                    delete preview.dataset.objectUrl;
                }
                preview.removeAttribute('src');
            }
        });
        if (panelHost) panelHost.innerHTML = '';
        setStatus('');
    }

    function setStatus(message, kind) {
        if (!statusEl) return;
        statusEl.textContent = message || '';
        statusEl.classList.remove('text-danger-600', 'text-ink-500', 'dark:text-ink-400');
        if (!message) return;
        if (kind === 'error') {
            statusEl.classList.add('text-danger-600');
        } else if (kind === 'success') {
            statusEl.classList.add('text-emerald-600');
        } else {
            statusEl.classList.add('text-ink-500', 'dark:text-ink-400');
        }
    }

    function updatePreviewButton() {
        if (!previewBtn) return;
        if (scanning) {
            previewBtn.disabled = true;
            return;
        }
        const needBack = !isPassport();
        const ready = hasFile(frontInput) && (!needBack || hasFile(backInput));
        previewBtn.disabled = !ready;
        previewBtn.title = ready
            ? 'Quét tự động thông tin từ ảnh đã chọn'
            : 'Vui lòng chọn đủ ảnh mặt trước và mặt sau';
    }

    // =================== OCR result rendering ===================

    function badgeHtml(kind) {
        const icons = {
            high: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            medium: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 9v4M12 17h.01"/><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/></svg>',
            low: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M15 9l-6 6M9 9l6 6"/></svg>',
            missing: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>'
        };
        const text = kind === 'high' ? 'Tin cậy cao'
            : kind === 'medium' ? 'Cần kiểm tra'
                : kind === 'low' ? 'Độ tin cậy thấp'
                    : 'Không đọc được';
        return (icons[kind] || icons.missing) + '<span>' + text + '</span>';
    }

    function kindOf(field) {
        if (!field) return null;
        if (!field.value || !String(field.value).trim()) return 'missing';
        if (field.confidence >= HIGH_CONFIDENCE) return 'high';
        if (field.confidence >= MIN_CONFIDENCE) return 'medium';
        return 'low';
    }

    // Ngày OCR có thể ở nhiều định dạng ("12/01/1990", "1990-01-12", ...).
    function toIsoDate(value) {
        if (!value) return null;
        const raw = String(value).trim();

        // yyyy-MM-dd hoặc yyyy/MM/dd
        let m = raw.match(/^(\d{4})[-/](\d{1,2})[-/](\d{1,2})$/);
        if (m) {
            return m[1] + '-' + m[2].padStart(2, '0') + '-' + m[3].padStart(2, '0');
        }

        // dd/MM/yyyy hoặc dd-MM-yyyy
        m = raw.match(/^(\d{1,2})[-/](\d{1,2})[-/](\d{4})$/);
        if (m) {
            return m[3] + '-' + m[2].padStart(2, '0') + '-' + m[1].padStart(2, '0');
        }

        // dd-MM-yyyy (kiểu Việt Nam có dấu gạch ngang trước 4 năm)
        m = raw.match(/^(\d{1,2})-(\d{1,2})-(\d{4})$/);
        if (m) {
            return m[3] + '-' + m[2].padStart(2, '0') + '-' + m[1].padStart(2, '0');
        }

        return null;
    }

    function toGender(value) {
        if (!value) return null;
        const raw = String(value).trim().toLowerCase();
        if (raw.indexOf('nam') === 0 && raw.indexOf('nữ') === -1 && raw.indexOf('nu') === -1) return 'male';
        if (raw.indexOf('nữ') === 0 || raw.indexOf('nu') === 0) return 'female';
        if (raw === 'male' || raw === 'female' || raw === 'other') return raw;
        return null;
    }

    // Điền giá trị vào ô input/select tương ứng.
    function applyFieldValue(ocrField, rawValue) {
        const inputName = FIELD_MAP[ocrField];
        if (!inputName) return;

        const input = form.querySelector('[name="' + inputName + '"]');
        if (!input) return;

        let value = rawValue == null ? '' : String(rawValue).trim();
        if (!value) return;

        if (DATE_FIELDS.indexOf(ocrField) !== -1) {
            value = toIsoDate(value);
            if (!value) return;
        }
        if (ocrField === 'gender') {
            value = toGender(value);
            if (!value) return;
        }

        input.value = value;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function renderPanel(data) {
        const fields = data.fields || {};

        const items = Object.keys(fields).map(function (key) {
            const f = fields[key] || {};
            const kind = kindOf(f);
            const pct = f.value ? Math.round((f.confidence || 0) * 100) + '%' : '—';
            const display = (f.value && String(f.value).trim()) ? f.value : 'Không đọc được';
            const emptyClass = (f.value && String(f.value).trim()) ? '' : ' is-empty';
            return '<li class="ocr-field-item">' +
                '<span class="ocr-field-name">' + escapeHtml(FIELD_LABELS[key] || key) + '</span>' +
                '<span class="ocr-field-value' + emptyClass + '">' + escapeHtml(display) + '</span>' +
                '<span class="ocr-badge ocr-badge-' + (kind || 'none') + '" title="Độ tin cậy ' + pct + '">' +
                badgeHtml(kind) +
                '</span>' +
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
            '<h3 class="ocr-panel-title">Kết quả nhận diện</h3>' +
            '<p class="ocr-panel-sub">Đã đọc được thông tin từ ảnh. Vui lòng kiểm tra và chỉnh sửa trước khi tạo User.</p>' +
            '</div>' +
            '<span class="ocr-panel-time">' + (data.processingTimeMs || 0) + ' ms</span>' +
            '</div>' +
            '<ul class="ocr-field-list">' + items + '</ul>' +
            warningHtml +
            '</div>';
    }

    function renderEmptyPanel(message) {
        if (!panelHost) return;
        panelHost.innerHTML =
            '<div class="ocr-panel ocr-panel-empty" role="status">' +
            '<p class="ocr-panel-sub">' + escapeHtml(message) + '</p>' +
            '</div>';
    }

    // =================== OCR preview ===================

    function setScanning(state) {
        scanning = state;
        if (previewBtn) previewBtn.disabled = state;
        if (previewLabel) {
            previewLabel.textContent = state ? 'Đang nhận diện…' : 'Bắt đầu nhận diện OCR';
        }
    }

    function getAntiForgeryToken() {
        const input = form.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    async function runPreview() {
        if (scanning) return;
        if (!hasFile(frontInput)) {
            setStatus('Vui lòng chọn ảnh mặt trước.', 'error');
            return;
        }

        const data = new FormData();
        data.append('documentType', docTypeSelect ? docTypeSelect.value : 'CCCD');
        if (hasFile(frontInput)) {
            data.append('frontImage', frontInput.files[0], frontInput.files[0].name);
        }
        if (hasFile(backInput)) {
            data.append('backImage', backInput.files[0], backInput.files[0].name);
        }

        setScanning(true);
        setStatus('Đang nhận diện thông tin trên ảnh…');

        try {
            const response = await fetch(previewUrl, {
                method: 'POST',
                body: data,
                headers: { 'RequestVerificationToken': getAntiForgeryToken() },
                credentials: 'same-origin'
            });

            const payload = await response.json().catch(function () { return null; });

            if (response.ok && payload && payload.ok) {
                renderPanel(payload.data);

                // Điền sẵn các trường đạt ngưỡng tin cậy (BR-14).
                const fields = payload.data.fields || {};
                Object.keys(FIELD_MAP).forEach(function (ocrField) {
                    const f = fields[ocrField];
                    if (!f) return;
                    const hasValue = !!(f.value && String(f.value).trim());
                    if (!hasValue) return;
                    // Giá trị sai định dạng vẫn được điền kèm cảnh báo để người dùng sửa.
                    if (!f.isValid) {
                        applyFieldValue(ocrField, f.value);
                        return;
                    }
                    if (f.confidence < MIN_CONFIDENCE) return;
                    applyFieldValue(ocrField, f.value);
                });

                setStatus('Đã nhận diện xong. Vui lòng kiểm tra thông tin trước khi tạo User.', 'success');
                if (window.Toast) {
                    Toast.show('Đã nhận diện xong. Vui lòng kiểm tra thông tin trước khi tạo User.', 'success');
                }
            } else {
                const message = (payload && payload.message)
                    ? payload.message
                    : 'Không đọc được thông tin trên ảnh. Bạn vẫn có thể nhập thủ công.';
                renderEmptyPanel(message);
                setStatus(message, 'error');
                if (window.Toast) Toast.show(message, 'error');
            }
        } catch (err) {
            renderEmptyPanel('Không thể kết nối tới máy chủ. Bạn vẫn có thể nhập thủ công.');
            setStatus('Không thể kết nối tới máy chủ. Vui lòng thử lại.', 'error');
            if (window.Toast) Toast.show('Không thể kết nối tới máy chủ. Vui lòng thử lại.', 'error');
        } finally {
            setScanning(false);
            updatePreviewButton();
        }
    }

    // =================== wiring ===================

    modeCards.forEach(function (card) {
        card.addEventListener('click', function () {
            setMode(card.dataset.modeOption);
        });
    });

    if (previewBtn) {
        previewBtn.addEventListener('click', runPreview);
    }

    [frontInput, backInput].forEach(function (input) {
        if (!input) return;

        input.addEventListener('change', function () {
            // Ảnh đổi thì kết quả cũ không còn đúng.
            if (panelHost) panelHost.innerHTML = '';
            setStatus('');

            const file = input.files && input.files[0];
            const preview = form.querySelector('[data-preview-for="' + input.name + '"]');
            if (preview) {
                if (!file) {
                    preview.classList.add('hidden');
                    if (preview.dataset.objectUrl) {
                        URL.revokeObjectURL(preview.dataset.objectUrl);
                        delete preview.dataset.objectUrl;
                    }
                    preview.removeAttribute('src');
                } else {
                    if (preview.dataset.objectUrl) URL.revokeObjectURL(preview.dataset.objectUrl);
                    const url = URL.createObjectURL(file);
                    preview.dataset.objectUrl = url;
                    preview.src = url;
                    preview.classList.remove('hidden');
                }
            }
            updatePreviewButton();
        });
    });

    if (docTypeSelect) {
        docTypeSelect.addEventListener('change', function () {
            const passport = isPassport();
            if (backSection) {
                backSection.classList.toggle('hidden', passport);
            }
            if (passport && backInput) {
                backInput.value = '';
                const preview = form.querySelector('[data-preview-for="BackImage"]');
                if (preview) {
                    preview.classList.add('hidden');
                    if (preview.dataset.objectUrl) {
                        URL.revokeObjectURL(preview.dataset.objectUrl);
                        delete preview.dataset.objectUrl;
                    }
                    preview.removeAttribute('src');
                }
            }
            updatePreviewButton();
        });
    }

    // Chống gửi trùng: khoá nút lưu trong lúc request đang chạy.
    form.addEventListener('submit', function () {
        if (submitBtn) {
            submitBtn.disabled = true;
            submitBtn.dataset.originalText = submitBtn.textContent;
        }
    });

    // Boot
    setMode(mode === 'ocr' ? 'ocr' : 'manual');
    updatePreviewButton();
})();
