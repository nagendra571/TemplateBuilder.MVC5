/*
 * TemplateBuilder.Editor.Mvc5 - template-editor.js
 * Reconstructed for the MVC5 fork (origin wwwroot asset absent - see BLOCKERS.md).
 * Vanilla JS (no jQuery requirement). Behaviors: find & replace in the body editor,
 * live preview (load columns, render preview), validate, toggle active, duplicate,
 * lazy version history, dark/light theme. All AJAX posts carry the MVC5 anti-forgery
 * header (RequestVerificationToken) read from the hidden form field.
 */
(function () {
    'use strict';

    function el(id) {
        return document.getElementById(id);
    }

    function antiForgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function postJson(url, payload, onOk, onError) {
        var xhr = new XMLHttpRequest();
        xhr.open('POST', url, true);
        xhr.setRequestHeader('Content-Type', 'application/json');
        var token = antiForgeryToken();
        if (token) {
            xhr.setRequestHeader('RequestVerificationToken', token);
        }
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            var body = null;
            try { body = JSON.parse(xhr.responseText); } catch (e) { /* non-JSON */ }
            if (xhr.status >= 200 && xhr.status < 300) {
                if (onOk) onOk(body);
            } else if (onError) {
                onError(xhr.status, body);
            }
        };
        xhr.send(JSON.stringify(payload || {}));
    }

    function showMessage(text, isError) {
        var host = el('tb-editor-host');
        if (!host) return;
        var msg = document.createElement('div');
        msg.className = isError ? 'tb-msg tb-msg-error' : 'tb-msg';
        msg.textContent = text;
        host.insertBefore(msg, host.firstChild);
        window.setTimeout(function () { msg.parentNode.removeChild(msg); }, 6000);
    }

    function applyTheme() {
        var host = el('tb-editor-host');
        if (!host) return;
        var saved = null;
        try { saved = localStorage.getItem('tb-editor-theme'); } catch (e) { /* storage unavailable */ }
        if (saved === 'dark') {
            host.classList.add('tb-theme-dark');
        } else {
            host.classList.remove('tb-theme-dark');
        }
    }

    function wireThemeToggle() {
        var toggle = el('tb-theme-toggle');
        if (!toggle) return;
        toggle.addEventListener('click', function () {
            var host = el('tb-editor-host');
            var dark = host.classList.toggle('tb-theme-dark');
            try { localStorage.setItem('tb-editor-theme', dark ? 'dark' : 'light'); } catch (e) { /* ignore */ }
        });
    }

    function wireFindReplace() {
        var body = el('tb-template-body');
        if (!body) return;
        var find = el('tb-find-text');
        var replace = el('tb-replace-text');
        var replaceAll = el('tb-replace-all');
        if (!find || !replace || !replaceAll) return;

        replaceAll.addEventListener('click', function () {
            var needle = find.value;
            if (!needle) return;
            var text = body.value;
            var escaped = needle.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
            var re = new RegExp(escaped, 'g');
            var count = (text.match(re) || []).length;
            if (count === 0) {
                showMessage('No matches for "' + needle + '".');
                return;
            }
            body.value = text.replace(re, replace.value);
            showMessage('Replaced ' + count + ' occurrence(s).');
        });

        find.addEventListener('keydown', function (e) {
            if (e.key !== 'Enter') return;
            var needle = find.value;
            if (!needle) return;
            var text = body.value;
            var idx = text.indexOf(needle);
            if (idx >= 0) {
                body.focus();
                body.setSelectionRange(idx, idx + needle.length);
            } else {
                showMessage('No matches for "' + needle + '".');
            }
        });
    }

    function wireLoadColumns() {
        var button = el('tb-load-columns');
        var select = el('tb-available-views');
        var modelJson = el('tb-preview-model-json');
        if (!button || !select || !modelJson) return;

        button.addEventListener('click', function () {
            var viewName = select.value;
            if (!viewName) return;
            postJson(
                '/Templates/Api/Views/' + encodeURIComponent(viewName) + '/Columns',
                {},
                function (columns) {
                    var sample = {};
                    if (Array.isArray(columns)) {
                        columns.forEach(function (c) {
                            sample[c.Name || ''] = c.IsNullable ? null : '';
                        });
                    }
                    modelJson.value = JSON.stringify(sample, null, 2);
                },
                function (status, body) {
                    showMessage((body && body.message) ? body.message : 'Failed to load columns (' + status + ').', true);
                }
            );
        });
    }

    function wireRenderPreview() {
        var button = el('tb-render-preview');
        var body = el('tb-template-body');
        var modelJson = el('tb-preview-model-json');
        var output = el('tb-preview-output');
        var loading = el('tb-preview-loading');
        var form = el('tb-template-form');
        if (!button || !body || !modelJson || !output || !form) return;

        button.addEventListener('click', function () {
            var id = el('Id') ? el('Id').value : '';
            if (!id) {
                showMessage('Save the template first - preview is only available for saved templates.', true);
                return;
            }
            var modelText = modelJson.value.trim();
            if (modelText && !isValidJson(modelText)) {
                showMessage('Model JSON is not valid JSON.', true);
                return;
            }
            if (loading) loading.style.display = '';
            output.innerHTML = '';
            postJson(
                '/Templates/' + encodeURIComponent(id) + '/Preview',
                { body: body.value, modelJson: modelText || null },
                function (result) {
                    if (loading) loading.style.display = 'none';
                    if (result && result.html !== undefined) {
                        output.innerHTML = result.html;
                    } else {
                        output.textContent = (result && result.message) ? result.message : 'Preview returned no output.';
                    }
                },
                function (status, body) {
                    if (loading) loading.style.display = 'none';
                    showMessage((body && body.message) ? body.message : 'Preview failed (' + status + ').', true);
                }
            );
        });
    }

    function isValidJson(text) {
        try {
            JSON.parse(text);
            return true;
        } catch (e) {
            return false;
        }
    }

    function wireVersionHistory() {
        var container = el('tb-version-history');
        if (!container) return;
        var url = container.getAttribute('data-url');
        if (!url) return;
        var xhr = new XMLHttpRequest();
        xhr.open('GET', url, true);
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            if (xhr.status === 200) {
                container.innerHTML = xhr.responseText;
                wireVersionHistoryButtons(container);
            } else {
                container.textContent = 'Version history failed to load (' + xhr.status + ').';
            }
        };
        xhr.send();
    }

    function wireVersionHistoryButtons(container) {
        var buttons = container.querySelectorAll('[data-restore-url]');
        for (var i = 0; i < buttons.length; i++) {
            buttons[i].addEventListener('click', function () {
                if (!window.confirm('Restore this version?')) return;
                postJson(
                    this.getAttribute('data-restore-url'),
                    {},
                    function () {
                        window.location.reload();
                    },
                    function (status, body) {
                        showMessage((body && body.message) ? body.message : 'Restore failed (' + status + ').', true);
                    }
                );
            });
        }
    }

    function wireValidate() {
        var button = el('tb-validate-template');
        var body = el('tb-template-body');
        var id = el('Id');
        if (!button || !body || !id) return;
        button.addEventListener('click', function () {
            postJson(
                '/Templates/' + encodeURIComponent(id.value) + '/Validate',
                { body: body.value },
                function (result) {
                    if (result && result.valid) {
                        showMessage('Template is valid.');
                    } else {
                        showMessage((result && result.message) ? 'Invalid: ' + result.message : 'Invalid template.', true);
                    }
                },
                function (status, body) {
                    showMessage((body && body.message) ? body.message : 'Validation failed (' + status + ').', true);
                }
            );
        });
    }

    function wireToggleActive() {
        var button = el('tb-toggle-active');
        var id = el('Id');
        if (!button || !id) return;
        button.addEventListener('click', function () {
            postJson(
                '/Templates/' + encodeURIComponent(id.value) + '/ToggleActive',
                {},
                function (result) {
                    showMessage('Template is now ' + (result && result.isActive ? 'active' : 'inactive') + '.');
                },
                function (status, body) {
                    showMessage((body && body.message) ? body.message : 'Toggle failed (' + status + ').', true);
                }
            );
        });
    }

    function wireDuplicate() {
        var button = el('tb-duplicate-template');
        var id = el('Id');
        if (!button || !id) return;
        button.addEventListener('click', function () {
            var name = window.prompt('Name for the duplicated template:');
            if (!name || !name.trim()) return;
            postJson(
                '/Templates/' + encodeURIComponent(id.value) + '/Duplicate',
                { newName: name.trim() },
                function (result) {
                    if (result && result.redirect) {
                        window.location.href = result.redirect;
                    } else {
                        showMessage('Template duplicated.');
                    }
                },
                function (status, body) {
                    showMessage((body && body.message) ? body.message : 'Duplicate failed (' + status + ').', true);
                }
            );
        });
    }

    function init() {
        applyTheme();
        wireThemeToggle();
        wireFindReplace();
        wireLoadColumns();
        wireRenderPreview();
        wireVersionHistory();
        wireValidate();
        wireToggleActive();
        wireDuplicate();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
