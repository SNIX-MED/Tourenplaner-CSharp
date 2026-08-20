(function () {
    'use strict';

    const input = document.getElementById('AdditionalProductSkus');
    const idsInput = document.getElementById('AdditionalProductIds');
    const addButton = document.getElementById('gawela-add-skus');
    const list = document.getElementById('gawela-member-list');
    const info = document.getElementById('gawela-sku-paste-info');

    if (!input || !idsInput || !addButton || !list) {
        return;
    }

    const summariesUrl = list.dataset.summariesUrl || '';
    const summariesBySkusUrl = addButton.dataset.resolveUrl || '';

    function parseSkuTokens(value) {
        return (value || '')
            .split(/[\r\n\t,;|]+/)
            .map(function (x) { return x.trim().replace(/^["']+|["']+$/g, ''); })
            .filter(Boolean);
    }

    function getIds() {
        return (idsInput.value || '').split(',').map(function (x) { return x.trim(); }).filter(Boolean);
    }

    function setIds(values) {
        const unique = [];
        const seen = new Set();
        (values || []).forEach(function (value) {
            const clean = String(value || '').trim();
            if (!clean || seen.has(clean)) return;
            seen.add(clean);
            unique.push(clean);
        });
        idsInput.value = unique.join(',');
    }

    function createMemberRow(product) {
        const row = document.createElement('div');
        row.className = 'border rounded px-3 py-2 mb-2 gawela-member-row d-flex align-items-center justify-content-between';
        row.dataset.productId = String(product.id);

        const description = document.createElement('div');
        const strong = document.createElement('strong');
        strong.textContent = product.sku || ('ID ' + product.id);
        description.appendChild(strong);
        description.appendChild(document.createTextNode(' – ' + (product.name || '') + ' '));

        const meta = document.createElement('span');
        meta.className = 'text-muted small';
        meta.textContent = '(ID ' + product.id + ')';
        description.appendChild(meta);
        row.appendChild(description);

        const remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'btn btn-sm btn-outline-danger gawela-remove-member';
        remove.title = 'Artikel aus Zuordnung entfernen';
        remove.innerHTML = '<i class="fa fa-times"></i> Entfernen';
        row.appendChild(remove);
        return row;
    }

    function renderRows(rows) {
        list.innerHTML = '';
        if (!rows || !rows.length) {
            const empty = document.createElement('div');
            empty.className = 'text-muted gawela-no-members';
            empty.textContent = 'Keine weiteren Artikel zugeordnet.';
            list.appendChild(empty);
            return;
        }
        rows.forEach(function (product) { list.appendChild(createMemberRow(product)); });
    }

    function renderMembers() {
        const ids = getIds();
        if (!ids.length) {
            renderRows([]);
            return Promise.resolve();
        }
        if (!summariesUrl) return Promise.reject(new Error('Summaries URL missing'));
        return fetch(summariesUrl + '?ids=' + encodeURIComponent(ids.join(',')), { credentials: 'same-origin' })
            .then(function (response) { return response.json(); })
            .then(renderRows);
    }

    function setMessage(message, isError) {
        if (!info) return;
        info.textContent = message || '';
        info.classList.toggle('text-danger', !!isError);
        info.classList.toggle('text-muted', !isError);
    }

    addButton.addEventListener('click', function () {
        const tokens = parseSkuTokens(input.value);
        if (!tokens.length) {
            setMessage('Bitte mindestens eine Artikelnummer eingeben.', true);
            return;
        }
        if (!summariesBySkusUrl) {
            setMessage('Die Artikelprüfung ist nicht verfügbar.', true);
            return;
        }

        addButton.disabled = true;
        fetch(summariesBySkusUrl + '?skus=' + encodeURIComponent(tokens.join('\n')), { credentials: 'same-origin' })
            .then(function (response) { return response.json(); })
            .then(function (result) {
                const rows = result && result.rows ? result.rows : [];
                setIds(getIds().concat(rows.map(function (x) { return x.id; })));

                const missing = result && result.missingSkus ? result.missingSkus : [];
                const duplicates = result && result.duplicateSkus ? result.duplicateSkus : [];
                const remaining = missing.concat(duplicates);
                input.value = remaining.join('\n');

                const messages = [];
                if (missing.length) messages.push('Nicht gefunden: ' + missing.join(', '));
                if (duplicates.length) messages.push('Mehrfach im Katalog vorhanden: ' + duplicates.join(', '));
                if (!messages.length) messages.push(rows.length + ' Artikel zur Zuordnungsliste hinzugefügt.');
                setMessage(messages.join(' · '), missing.length > 0 || duplicates.length > 0);
                return renderMembers();
            })
            .catch(function () {
                setMessage('Artikel konnten nicht geprüft werden. Bitte erneut versuchen.', true);
            })
            .finally(function () { addButton.disabled = false; });
    });

    list.addEventListener('click', function (event) {
        const button = event.target.closest('.gawela-remove-member');
        if (!button) return;
        const row = button.closest('.gawela-member-row');
        if (!row) return;
        const productId = row.dataset.productId;
        setIds(getIds().filter(function (id) { return id !== productId; }));
        renderMembers();
    });

    // The Smartstore product picker writes IDs into the same hidden field.
    // Override the legacy callback so it only refreshes the authoritative lower list.
    window.GawelaMembers_Completed = function () {
        renderMembers();
        return true;
    };

    // The staging field must always start empty, even after browser form restoration.
    input.value = '';
    setMessage('');
})();