(function () {
    'use strict';

    const MOBILE_QUERY = '(max-width: 767.98px)';
    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
    const norm = value => (value || '').replace(/\s+/g, ' ').trim().toLowerCase();
    const ralFrom = value => {
        const match = (value || '').match(/RAL\s*[-:]?\s*(\d{4})/i) || (value || '').match(/\b(\d{4})\b/);
        return match ? match[1] : null;
    };

    function isMobile() {
        return window.matchMedia ? window.matchMedia(MOBILE_QUERY).matches : window.innerWidth < 768;
    }

    function productId() {
        const element = document.querySelector('#main-update-container[data-id]');
        const id = parseInt(element && element.dataset.id, 10);
        return Number.isFinite(id) && id > 0 ? id : null;
    }

    function choice(label) {
        const wanted = norm(label);
        for (const element of document.querySelectorAll('.pd-variants .choice')) {
            const actual = norm(element.querySelector('.choice-label')?.textContent);
            if (actual && (actual === wanted || actual.includes(wanted) || wanted.includes(actual))) {
                return element;
            }
        }
        return null;
    }

    function choiceText(element) {
        if (!element) return '';
        const select = element.querySelector('select');
        if (select) {
            return [select.selectedOptions?.[0]?.textContent, select.value].join(' ');
        }

        const input = element.querySelector('input[type=radio]:checked,input[type=checkbox]:checked');
        if (!input) return '';
        const label = input.id ? element.querySelector('label[for="' + CSS.escape(input.id) + '"]') : null;
        return [
            input.getAttribute('aria-label'),
            input.title,
            input.value,
            label?.textContent,
            label?.getAttribute('title'),
            label?.querySelector('[title]')?.getAttribute('title')
        ].join(' ');
    }

    function selected(layer) {
        return ralFrom(choiceText(choice(layer.attributeLabel))) || layer.defaultRal;
    }


    function semanticSlug(value) {
        return (value || '')
            .trim()
            .toLowerCase()
            .replace(/ä/g, 'ae')
            .replace(/ö/g, 'oe')
            .replace(/ü/g, 'ue')
            .replace(/ß/g, 'ss')
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '');
    }

    function syncSemanticUrl(state) {
        try {
            const url = new URL(window.location.href);
            for (const key of [...url.searchParams.keys()]) {
                if (key.startsWith('farbe-')) url.searchParams.delete(key);
            }

            const used = new Set();
            state.layers.forEach((layer, index) => {
                let ral = selected(layer);
                if (!state.colors[ral]) ral = layer.defaultRal;
                if (!state.colors[ral]) return;

                const color = state.colors[ral];
                let area = semanticSlug(layer.name || layer.attributeLabel || ('bereich-' + (index + 1)));
                if (!area) area = 'bereich-' + (index + 1);
                if (used.has(area)) area += '-' + (index + 1);
                used.add(area);

                const colorName = semanticSlug(color.name || '');
                const semanticValue = 'ral-' + ral + (colorName ? '-' + colorName : '');
                url.searchParams.set('farbe-' + area, semanticValue);
            });

            const next = url.pathname + (url.searchParams.toString() ? '?' + url.searchParams.toString() : '') + url.hash;
            const current = window.location.pathname + window.location.search + window.location.hash;
            if (next !== current) history.replaceState(history.state, '', next);
        } catch (_) {
            // Semantic URL enrichment must never interfere with the configurator itself.
        }
    }

    function assetUrl(endpoint, id, kind) {
        return endpoint + (endpoint.includes('?') ? '&' : '?') +
            'productId=' + id + '&kind=' + encodeURIComponent(kind) + '&v=' + Date.now();
    }

    function loadImage(src) {
        return new Promise((resolve, reject) => {
            const image = new Image();
            image.onload = () => resolve(image);
            image.onerror = () => reject(new Error('Bild konnte nicht geladen werden: ' + src));
            image.src = src;
        });
    }

    async function json(src) {
        const response = await fetch(src, { credentials: 'same-origin' });
        if (!response.ok) throw new Error('Konfiguration nicht verfügbar.');
        return response.json();
    }

    async function palette(src) {
        const data = await json(src);
        return data.colors || data;
    }

    function pixels(image, width, height) {
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext('2d', { willReadFrequently: true });
        context.drawImage(image, 0, 0, width, height);
        return context.getImageData(0, 0, width, height);
    }

    const luminance = (r, g, b) => .2126 * r + .7152 * g + .0722 * b;
    const clamp = value => Math.max(0, Math.min(255, Math.round(value)));

    function strength(mask, index) {
        return ((mask[index] + mask[index + 1] + mask[index + 2]) / 765) * (mask[index + 3] / 255);
    }

    function averageLuma(base, mask) {
        let sum = 0;
        let weight = 0;
        for (let i = 0; i < base.length; i += 4) {
            const amount = strength(mask, i);
            if (amount > .01) {
                sum += luminance(base[i], base[i + 1], base[i + 2]) * amount;
                weight += amount;
            }
        }
        return weight ? sum / weight : 180;
    }

    function tinted(basePixel, rgb, reference) {
        const factor = Math.max(.35, Math.min(1.65,
            luminance(basePixel[0], basePixel[1], basePixel[2]) / Math.max(reference, 1)));
        return [clamp(rgb[0] * factor), clamp(rgb[1] * factor), clamp(rgb[2] * factor)];
    }

    function draw(state) {
        const base = state.base.data;
        const output = new ImageData(new Uint8ClampedArray(base), state.w, state.h);
        const data = output.data;
        const labels = [];

        for (const layer of state.layers) {
            let ral = selected(layer);
            if (!state.colors[ral]) ral = layer.defaultRal;
            if (!state.colors[ral]) continue;

            const color = state.colors[ral];
            const mask = layer.mask.data;
            labels.push(layer.name + ': RAL ' + ral + (color.name ? ' ' + color.name : ''));

            if (ral === layer.baseRal) continue;

            for (let i = 0; i < base.length; i += 4) {
                const amount = strength(mask, i);
                if (amount <= .01) continue;
                const target = tinted(
                    [base[i], base[i + 1], base[i + 2]],
                    color.rgb,
                    layer.refLuma
                );
                data[i] = clamp(data[i] * (1 - amount) + target[0] * amount);
                data[i + 1] = clamp(data[i + 1] * (1 - amount) + target[1] * amount);
                data[i + 2] = clamp(data[i + 2] * (1 - amount) + target[2] * amount);
            }
        }

        state.ctx.putImageData(output, 0, 0);
        if (state.info) state.info.textContent = labels.join(' · ');
        state.currentLabel = labels.join(' · ');
        syncSemanticUrl(state);
        updateMobilePreview(state);
    }

    function gallery() {
        const root = document.querySelector('#pd-gallery');
        const $ = window.jQuery;
        if (!root || !$) return null;
        const instance = $(root).data('smartGallery');
        if (!instance || !instance.gallery) return null;
        const main = root.querySelector('.gal');
        const nav = root.querySelector('.gal-nav .gal-track');
        return main && nav ? { root, instance, main, nav } : null;
    }

    function createSlide(state) {
        const item = document.createElement('div');
        item.className = 'gal-item gawela-gallery-slide';
        item.setAttribute('role', 'listitem');

        const viewport = document.createElement('div');
        viewport.className = 'gal-item-viewport gawela-gallery-viewport';

        const stage = document.createElement('div');
        stage.className = 'gawela-color-stage';

        const canvas = document.createElement('canvas');
        canvas.className = 'gawela-color-canvas';
        canvas.width = state.w;
        canvas.height = state.h;
        stage.appendChild(canvas);

        const badge = document.createElement('div');
        badge.className = 'gawela-config-badge';
        badge.innerHTML = '<i class="fa fa-palette"></i><span>' + state.thumb + '</span>';
        stage.appendChild(badge);
        viewport.appendChild(stage);

        const footer = document.createElement('div');
        footer.className = 'gawela-color-footer';

        const info = document.createElement('div');
        info.className = 'gawela-color-meta';
        footer.appendChild(info);

        const disclaimer = document.createElement('small');
        disclaimer.className = 'gawela-color-disclaimer';
        disclaimer.textContent = 'Bildschirmdarstellung unverbindlich; Farbe, Proportionen, Details und Ausführung können abweichen.';
        footer.appendChild(disclaimer);
        viewport.appendChild(footer);
        item.appendChild(viewport);

        state.ctx = canvas.getContext('2d', { willReadFrequently: true });
        state.info = info;
        return item;
    }

    function createThumb(state) {
        const item = document.createElement('div');
        item.className = 'gal-item gawela-gallery-thumb';

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'gal-item-viewport gawela-gallery-thumb-button';
        button.title = state.thumb;
        button.setAttribute('aria-label', state.thumb);
        button.setAttribute('role', 'option');

        const image = document.createElement('img');
        image.className = 'gal-item-content gawela-gallery-thumb-image';
        image.src = state.baseUrl;
        image.alt = state.thumb;
        button.appendChild(image);

        const icon = document.createElement('span');
        icon.className = 'gawela-gallery-thumb-icon';
        icon.innerHTML = '<i class="fa fa-palette"></i>';
        button.appendChild(icon);
        item.appendChild(button);
        return item;
    }

    function install(state) {
        let currentGallery = gallery();
        if (!currentGallery) return false;

        const existing = currentGallery.root.querySelector('.gawela-gallery-slide:not(.slick-cloned)');
        if (existing) {
            const index = parseInt(existing.getAttribute('data-slick-index'), 10);
            if (Number.isFinite(index)) state.index = index;
            return true;
        }

        const currentIndex = Number.isFinite(currentGallery.instance.currentIndex)
            ? currentGallery.instance.currentIndex
            : 0;

        currentGallery.instance.reset();

        const root = document.querySelector('#pd-gallery');
        const main = root?.querySelector('.gal');
        const nav = root?.querySelector('.gal-nav .gal-track');
        if (!main || !nav) return false;

        main.querySelectorAll(':scope > .gawela-gallery-slide').forEach(x => x.remove());
        nav.querySelectorAll(':scope > .gawela-gallery-thumb').forEach(x => x.remove());

        const count = main.querySelectorAll(':scope > .gal-item').length;
        main.appendChild(createSlide(state));
        nav.appendChild(createThumb(state));
        state.index = count;

        // When a colour change is pending, make the configurator the active
        // gallery slide on every device. On mobile the document itself is not
        // scrolled; only the gallery's internal selection changes.
        const jumpToConfigurator = state.pending;
        currentGallery.instance.options.startIndex = jumpToConfigurator
            ? count
            : Math.min(currentIndex, Math.max(count - 1, 0));

        currentGallery.instance.init();
        root.querySelector('.gal-nav-cell')?.classList.remove('gal-nav-hidden');
        draw(state);
        refreshMobilePreviewObservers(state);
        return true;
    }

    function jump(state) {
        const currentGallery = gallery();
        if (currentGallery && Number.isFinite(state.index)) {
            try {
                currentGallery.instance.goTo(state.index);
            } catch (error) {
                console.debug('GAWELA:', error);
            }
        }
    }

    function selectConfiguratorSilently(state) {
        // Mobile UX 6.4.14: select the configurator slide in the gallery without
        // moving the customer's document scroll position away from the options.
        // This means that manual scrolling back to the gallery always reveals
        // the current configured image instead of the last normal product photo.
        if (!isMobile()) {
            jump(state);
            return;
        }

        const x = window.scrollX;
        const y = window.scrollY;
        jump(state);

        const restore = () => {
            if (Math.abs(window.scrollX - x) > 1 || Math.abs(window.scrollY - y) > 1) {
                window.scrollTo({ left: x, top: y, behavior: 'auto' });
            }
        };
        restore();
        requestAnimationFrame(restore);
    }

    async function waitForGallery(state, attempts = 60) {
        for (let i = 0; i < attempts; i++) {
            if (install(state)) return true;
            await sleep(50);
        }
        return false;
    }

    function relevant(state, element) {
        const label = norm(element.querySelector('.choice-label')?.textContent);
        return state.layers.some(layer => {
            const wanted = norm(layer.attributeLabel);
            return label === wanted || label.includes(wanted) || wanted.includes(label);
        });
    }

    function unfreeze(state) {
        if (state.freeze) {
            state.freeze.remove();
            state.freeze = null;
        }
    }

    function freeze(state) {
        // The freeze overlay is a desktop-only transition aid. On mobile the
        // user stays at the variants and sees the dedicated sticky live preview.
        if (isMobile()) return;

        unfreeze(state);
        const currentGallery = gallery();
        if (!currentGallery || !Number.isFinite(state.index) || currentGallery.instance.currentIndex !== state.index) return;

        const source = currentGallery.root.querySelector('.gawela-gallery-slide.slick-current:not(.slick-cloned) .gawela-gallery-viewport') ||
            currentGallery.root.querySelector('.gawela-gallery-slide:not(.slick-cloned) .gawela-gallery-viewport');
        if (!source) return;

        const rect = currentGallery.main.getBoundingClientRect();
        if (!rect.width || !rect.height) return;

        const overlay = document.createElement('div');
        overlay.className = 'gawela-color-freeze';
        overlay.style.left = rect.left + 'px';
        overlay.style.top = rect.top + 'px';
        overlay.style.width = rect.width + 'px';
        overlay.style.height = rect.height + 'px';

        const clone = source.cloneNode(true);
        const sourceCanvas = source.querySelector('canvas');
        const destinationCanvas = clone.querySelector('canvas');
        if (sourceCanvas && destinationCanvas) {
            const image = document.createElement('img');
            image.className = 'gawela-color-canvas';
            try { image.src = sourceCanvas.toDataURL('image/png'); } catch { }
            destinationCanvas.replaceWith(image);
        }

        overlay.appendChild(clone);
        document.body.appendChild(overlay);
        state.freeze = overlay;
    }

    function finish(state) {
        if (isMobile()) selectConfiguratorSilently(state);
        else jump(state);
        requestAnimationFrame(() => requestAnimationFrame(() => unfreeze(state)));
        state.pending = false;
        updateMobilePreview(state);
        updateMobilePreviewVisibility(state);
    }

    // ---------------------------------------------------------------------
    // Mobile UX (6.4.14)
    // ---------------------------------------------------------------------
    function ensureMobilePreview(state) {
        if (state.mobilePreview?.isConnected) return state.mobilePreview;

        const card = document.createElement('button');
        card.type = 'button';
        card.className = 'gawela-mobile-preview';
        card.hidden = true;
        card.setAttribute('aria-label', 'Aktuelle Farbvorschau öffnen');
        card.setAttribute('aria-live', 'polite');

        const imageWrap = document.createElement('span');
        imageWrap.className = 'gawela-mobile-preview-image';
        const canvas = document.createElement('canvas');
        const scale = 240 / Math.max(state.w, state.h);
        canvas.width = Math.max(1, Math.round(state.w * scale));
        canvas.height = Math.max(1, Math.round(state.h * scale));
        imageWrap.appendChild(canvas);
        card.appendChild(imageWrap);

        const text = document.createElement('span');
        text.className = 'gawela-mobile-preview-copy';
        const title = document.createElement('strong');
        title.className = 'gawela-mobile-preview-title';
        title.textContent = 'Live-Farbvorschau';
        const meta = document.createElement('span');
        meta.className = 'gawela-mobile-preview-meta';
        text.appendChild(title);
        text.appendChild(meta);
        card.appendChild(text);

        const icon = document.createElement('span');
        icon.className = 'gawela-mobile-preview-open';
        icon.setAttribute('aria-hidden', 'true');
        icon.innerHTML = '<i class="fa fa-expand-alt"></i>';
        card.appendChild(icon);

        card.addEventListener('click', async () => {
            await waitForGallery(state, 20);
            jump(state);
            const target = document.querySelector('#pd-gallery-container') || document.querySelector('#pd-gallery');
            target?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        });

        document.body.appendChild(card);
        state.mobilePreview = card;
        state.mobilePreviewCanvas = canvas;
        state.mobilePreviewCtx = canvas.getContext('2d');
        state.mobilePreviewMeta = meta;
        return card;
    }

    function updateMobilePreview(state) {
        if (!isMobile() && !state.mobilePreview) return;
        const card = ensureMobilePreview(state);
        if (!state.mobilePreviewCtx || !state.ctx?.canvas) return;

        const canvas = state.mobilePreviewCanvas;
        const context = state.mobilePreviewCtx;
        context.clearRect(0, 0, canvas.width, canvas.height);
        context.drawImage(state.ctx.canvas, 0, 0, canvas.width, canvas.height);
        state.mobilePreviewMeta.textContent = state.currentLabel || 'Aktuelle Farbauswahl';
        card.dataset.ready = 'true';
    }

    function updateMobilePreviewTopOffset(card) {
        let bottom = 0;
        for (const element of document.querySelectorAll('header, .header, .navbar, .nav-main, .site-header')) {
            if (element === card || card.contains(element)) continue;
            const style = window.getComputedStyle(element);
            if (style.position !== 'fixed' && style.position !== 'sticky') continue;
            const rect = element.getBoundingClientRect();
            if (rect.width < window.innerWidth * .5 || rect.height <= 0 || rect.height > 180) continue;
            if (rect.top <= 4 && rect.bottom > 0) bottom = Math.max(bottom, rect.bottom);
        }
        card.style.top = Math.max(7, Math.round(bottom + 7)) + 'px';
    }

    function updateMobilePreviewVisibility(state) {
        const card = state.mobilePreview;
        if (!card) return;

        if (!isMobile()) {
            card.hidden = true;
            return;
        }

        updateMobilePreviewTopOffset(card);
        const galleryContainer = document.querySelector('#pd-gallery-container') || document.querySelector('#pd-gallery');
        const form = document.querySelector('#pd-form') || document.querySelector('#main-update-container');
        if (!galleryContainer || !form) {
            card.hidden = true;
            return;
        }

        const galleryRect = galleryContainer.getBoundingClientRect();
        const formRect = form.getBoundingClientRect();
        const hasPassedGallery = galleryRect.bottom <= 8;
        const stillInsideProductForm = formRect.bottom > 80;
        const ready = card.dataset.ready === 'true';

        card.hidden = !(ready && hasPassedGallery && stillInsideProductForm);
    }

    function refreshMobilePreviewObservers(state) {
        ensureMobilePreview(state);

        if (state.mobileObserver) state.mobileObserver.disconnect();
        const targets = [
            document.querySelector('#pd-gallery-container') || document.querySelector('#pd-gallery'),
            document.querySelector('#pd-form') || document.querySelector('#main-update-container')
        ].filter(Boolean);

        if ('IntersectionObserver' in window && targets.length) {
            state.mobileObserver = new IntersectionObserver(() => {
                updateMobilePreviewVisibility(state);
            }, { threshold: [0, .01, .25, 1] });
            targets.forEach(target => state.mobileObserver.observe(target));
        }

        if (!state.mobileScrollBound) {
            let scheduled = false;
            const refresh = () => {
                if (scheduled) return;
                scheduled = true;
                requestAnimationFrame(() => {
                    scheduled = false;
                    updateMobilePreviewVisibility(state);
                });
            };
            window.addEventListener('scroll', refresh, { passive: true });
            window.addEventListener('resize', refresh, { passive: true });
            if (window.matchMedia) {
                const media = window.matchMedia(MOBILE_QUERY);
                if (media.addEventListener) media.addEventListener('change', refresh);
                else if (media.addListener) media.addListener(refresh);
            }
            state.mobileScrollBound = true;
        }

        updateMobilePreview(state);
        updateMobilePreviewVisibility(state);
    }

    function bind(state) {
        document.addEventListener('change', event => {
            const element = event.target.closest?.('.pd-variants .choice');
            if (!element || !relevant(state, element)) return;

            state.pending = true;

            if (isMobile()) {
                // Mobile: stay exactly where the customer is configuring, but
                // silently activate the configurator slide in the gallery. The
                // sticky mini preview remains the immediate visual feedback.
                draw(state);
                selectConfiguratorSilently(state);
                updateMobilePreviewVisibility(state);
                setTimeout(() => {
                    install(state);
                    draw(state);
                    selectConfiguratorSilently(state);
                    updateMobilePreviewVisibility(state);
                }, 250);
                setTimeout(async () => {
                    if (!state.pending) return;
                    await waitForGallery(state, 30);
                    draw(state);
                    finish(state);
                }, 3000);
                return;
            }

            // Desktop behaviour remains unchanged.
            jump(state);
            draw(state);
            requestAnimationFrame(() => freeze(state));
            setTimeout(() => {
                install(state);
                draw(state);
                jump(state);
            }, 250);
            setTimeout(async () => {
                if (!state.pending) return;
                await waitForGallery(state, 30);
                draw(state);
                finish(state);
            }, 3000);
        }, true);

        if (window.jQuery) {
            window.jQuery('#main-update-container')
                .off('updated.gawela6414')
                .on('updated.gawela6414', async () => {
                    await waitForGallery(state, 30);
                    draw(state);
                    if (state.pending && isMobile()) selectConfiguratorSilently(state);
                    refreshMobilePreviewObservers(state);
                    if (state.pending) finish(state);
                });
        }
    }

    function pick(object, name) {
        if (!object) return undefined;
        const pascal = name.charAt(0).toUpperCase() + name.slice(1);
        return object[name] !== undefined ? object[name] : object[pascal];
    }

    function normalizeConfig(raw) {
        const layers = (pick(raw, 'layers') || []).map(layer => ({
            key: pick(layer, 'key'),
            name: pick(layer, 'name') || 'Ebene',
            productVariantAttributeId: pick(layer, 'productVariantAttributeId'),
            attributeLabel: pick(layer, 'attributeLabel') || '',
            assetKind: pick(layer, 'assetKind') || '',
            baseRal: String(pick(layer, 'baseRal') || '7035'),
            defaultRal: String(pick(layer, 'defaultRal') || '7035')
        }));
        return {
            thumbnailLabel: pick(raw, 'thumbnailLabel') || 'Farbe konfigurieren',
            layers
        };
    }

    async function init(host) {
        if (host.dataset.gawelaInit) return;
        host.dataset.gawelaInit = '1';

        const id = productId();
        if (!id) return;

        const assetEndpoint = host.dataset.assetEndpoint;
        const configEndpoint = host.dataset.configEndpoint +
            (host.dataset.configEndpoint.includes('?') ? '&' : '?') + 'productId=' + id;

        try {
            const [rawConfig, colors] = await Promise.all([
                json(configEndpoint),
                palette(host.dataset.paletteUrl)
            ]);
            const config = normalizeConfig(rawConfig);
            if (!config.layers.length) {
                throw new Error('Keine Visualisierungsebenen in der Produktkonfiguration gefunden.');
            }

            const baseUrl = assetUrl(assetEndpoint, id, 'base');
            const baseImage = await loadImage(baseUrl);
            const width = baseImage.naturalWidth;
            const height = baseImage.naturalHeight;
            if (!width || !height) throw new Error('Basisbild ungültig.');

            const base = pixels(baseImage, width, height);
            const layers = [];
            for (const layer of config.layers) {
                const maskImage = await loadImage(assetUrl(assetEndpoint, id, layer.assetKind));
                if (maskImage.naturalWidth !== width || maskImage.naturalHeight !== height) {
                    throw new Error('Maske „' + layer.name + '“ hat andere Bildmasse.');
                }
                const mask = pixels(maskImage, width, height);
                layers.push({ ...layer, mask, refLuma: averageLuma(base.data, mask) });
            }

            const state = {
                id,
                w: width,
                h: height,
                base,
                layers,
                colors,
                baseUrl,
                thumb: config.thumbnailLabel || 'Farbe konfigurieren',
                ctx: null,
                info: null,
                index: null,
                pending: false,
                freeze: null,
                currentLabel: '',
                mobilePreview: null,
                mobilePreviewCanvas: null,
                mobilePreviewCtx: null,
                mobilePreviewMeta: null,
                mobileObserver: null,
                mobileScrollBound: false
            };

            if (!await waitForGallery(state)) {
                throw new Error('Smartstore-Produktgalerie nicht verfügbar.');
            }

            draw(state);
            refreshMobilePreviewObservers(state);
            bind(state);
        } catch (error) {
            console.warn('GAWELA Farbkonfigurator:', error?.message || error);
        }
    }

    const boot = () => document.querySelectorAll('.gawela-color-host').forEach(init);
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
})();
