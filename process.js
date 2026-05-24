// process.js

const axios = require('axios');
const Jimp = require('jimp');
const fs = require('fs');
const path = require('path');
const {
    execSync
} = require('child_process');

async function downloadDEM(bbox, apiKey, outputDir) {
    const {
        south,
        north,
        west,
        east
    } = bbox;
    console.log(`[Топологическая карта GeoTIFF] Запрос: ${west},${south} → ${east},${north}`);

    const response = await axios.get('https://portal.opentopography.org/API/globaldem', {
        params: {
            demtype: 'COP30',
            south,
            north,
            west,
            east,
            outputFormat: 'GTiff',
            API_Key: apiKey
        },
        responseType: 'arraybuffer',
        timeout: 120000,
    });

    // Сохраняем оригинальный (возможно сжатый) TIF
    const compressedTif = path.join(outputDir, 'heightmap_compressed.tif');
    fs.writeFileSync(compressedTif, response.data);
    console.log(`[Топологическая карта GeoTIFF] Скачана (${(response.data.byteLength / 1024).toFixed(0)} KB)`);

    // Распаковываем через gdal_translate
    const tifPath = path.join(outputDir, 'heightmap.tif');
    console.log(`[Топологическая карта GeoTIFF] Распаковка...`);
    execSync(`gdal_translate -co COMPRESS=NONE "${compressedTif}" "${tifPath}"`, {
        stdio: 'inherit'
    });
    fs.unlinkSync(compressedTif);

    console.log(`[Топологическая карта GeoTIFF] Готова: ${tifPath}`);
    return tifPath;
}

function parseTiff(data) {
    const le = data[0] === 0x49;

    const r16 = o => le ? (data[o] | data[o + 1] << 8) : (data[o] << 8 | data[o + 1]);
    const r32 = o => le ?
        ((data[o] | data[o + 1] << 8 | data[o + 2] << 16 | data[o + 3] << 24) >>> 0) :
        ((data[o] << 24 | data[o + 1] << 16 | data[o + 2] << 8 | data[o + 3]) >>> 0);
    const rF32 = o => {
        const b = Buffer.allocUnsafe(4);
        b[0] = data[o];
        b[1] = data[o + 1];
        b[2] = data[o + 2];
        b[3] = data[o + 3];
        return le ? b.readFloatLE(0) : b.readFloatBE(0);
    };

    const readVals = (type, count, vp) => {
        const stride = type === 4 ? 4 : 2;
        const readOne = o => type === 4 ? r32(o) : r16(o);
        if (count === 1) return [readOne(vp)];
        const ptr = r32(vp);
        return Array.from({
            length: count
        }, (_, i) => readOne(ptr + i * stride));
    };

    const ifd = r32(4);
    const n = r16(ifd);

    let W = 0,
        H = 0,
        bps = 32,
        fmt = 3,
        compression = 1;
    let tileW = 0,
        tileH = 0;
    let tileOffsets = [],
        tileByteCounts = [];
    let stripOffsets = [],
        stripByteCounts = [];

    for (let i = 0; i < n; i++) {
        const ep = ifd + 2 + i * 12;
        const tag = r16(ep);
        const type = r16(ep + 2);
        const count = r32(ep + 4);
        const vp = ep + 8;

        switch (tag) {
            case 256:
                W = readVals(type, count, vp)[0];
                break;
            case 257:
                H = readVals(type, count, vp)[0];
                break;
            case 258:
                bps = readVals(type, count, vp)[0];
                break;
            case 259:
                compression = readVals(type, count, vp)[0];
                break;
            case 322:
                tileW = readVals(type, count, vp)[0];
                break;
            case 323:
                tileH = readVals(type, count, vp)[0];
                break;
            case 324:
                tileOffsets = readVals(type, count, vp);
                break;
            case 325:
                tileByteCounts = readVals(type, count, vp);
                break;
            case 273:
                stripOffsets = readVals(type, count, vp);
                break;
            case 279:
                stripByteCounts = readVals(type, count, vp);
                break;
            case 339:
                fmt = readVals(type, count, vp)[0];
                break;
        }
    }

    const isTiled = tileOffsets.length > 0;

    if (compression !== 1) {
        throw new Error(`TIF сжат (compression=${compression}), gdal_translate не отработал`);
    }

    const bpp = bps / 8;
    const pixels = new Array(W * H).fill(null);
    let min = Infinity,
        max = -Infinity;

    if (isTiled) {
        const tilesX = Math.ceil(W / tileW);
        const tilesY = Math.ceil(H / tileH);
        for (let ty = 0; ty < tilesY; ty++) {
            for (let tx = 0; tx < tilesX; tx++) {
                const tileIdx = ty * tilesX + tx;
                let pos = tileOffsets[tileIdx];

                for (let row = 0; row < tileH; row++) {
                    const imgY = ty * tileH + row;
                    if (imgY >= H) break;
                    for (let col = 0; col < tileW; col++) {
                        const imgX = tx * tileW + col;
                        if (imgX >= W) {
                            pos += bpp;
                            continue;
                        }
                        let v;
                        if (fmt === 3) v = rF32(pos);
                        else if (fmt === 2) v = data.readInt16LE(pos);
                        else v = r16(pos);
                        pos += bpp;
                        if (v < -9999 || v > 15000) continue;
                        pixels[imgY * W + imgX] = v;
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                }
            }
        }
    } else {
        let pixelIdx = 0;
        for (let s = 0; s < stripOffsets.length; s++) {
            let pos = stripOffsets[s];
            const end = pos + (stripByteCounts[s] || W * bpp);
            while (pos + bpp <= end && pixelIdx < W * H) {
                let v;
                if (fmt === 3) v = rF32(pos);
                else if (fmt === 2) v = data.readInt16LE(pos);
                else v = r16(pos);
                pos += bpp;
                if (v < -9999 || v > 15000) {
                    pixelIdx++;
                    continue;
                }
                pixels[pixelIdx] = v;
                if (v < min) min = v;
                if (v > max) max = v;
                pixelIdx++;
            }
        }
    }

    console.log(`[Карта высот TIF] Высоты: ${min.toFixed(1)}–${max.toFixed(1)} м`);
    return {
        pixels,
        W,
        H,
        min,
        max
    };
}

async function convertDEM(tifPath, res, outputDir) {
    const {
        pixels,
        W,
        H,
        min,
        max
    } = parseTiff(fs.readFileSync(tifPath));
    const range = (max - min) || 1;

    const sample = (px, py) => {
        const x = Math.min(Math.floor(px * (W - 1) / (res - 1)), W - 1);
        const y = Math.min(Math.floor(py * (H - 1) / (res - 1)), H - 1);
        return pixels[y * W + x] ?? min;
    };

    const raw = Buffer.alloc(res * res * 2);
    for (let py = 0; py < res; py++)
        for (let px = 0; px < res; px++) {
            const val = Math.round(((sample(px, py) - min) / range) * 65535);
            const i = (py * res + px) * 2;
            raw[i] = val & 0xFF;
            raw[i + 1] = (val >> 8) & 0xFF;
        }
    const rawPath = path.join(outputDir, 'height.raw');
    fs.writeFileSync(rawPath, raw);

    const img = new Jimp(res, res);
    for (let py = 0; py < res; py++)
        for (let px = 0; px < res; px++) {
            const g = Math.round(((sample(px, py) - min) / range) * 255);
            img.setPixelColor(Jimp.rgbaToInt(g, g, g, 255), px, py);
        }
    const previewPath = path.join(outputDir, 'heightmap_preview.png');
    await img.writeAsync(previewPath);
    console.log(`[Топологическая карта GeoTIFF] Конвертация завершена`);

    return {
        rawPath,
        previewPath,
        min,
        max
    };
}

function deg2tile(lat, lng, z) {
    return {
        x: Math.floor((lng + 180) / 360 * 2 ** z),
        y: Math.floor((1 - Math.log(Math.tan(lat * Math.PI / 180) + 1 / Math.cos(lat * Math.PI / 180)) / Math.PI) / 2 * 2 ** z)
    };
}

function tile2lng(x, z) {
    return x / 2 ** z * 360 - 180;
}

function tile2lat(y, z) {
    return Math.atan(Math.sinh(Math.PI * (1 - 2 * y / 2 ** z))) * 180 / Math.PI;
}

async function downloadTexture(bbox, res, outputDir) {
    const {
        south,
        north,
        west,
        east
    } = bbox;
    const span = north - south;
    const zoom = span > 1 ? 10 : span > 0.5 ? 11 : span < 0.1 ? 15 : 13;

    const tl = deg2tile(north, west, zoom);
    const br = deg2tile(south, east, zoom);
    const S = 256,
        TW = br.x - tl.x + 1,
        TH = br.y - tl.y + 1;

    const tileResults = await Promise.all(
        Array.from({
            length: TW * TH
        }, async (_, i) => {
            const tx = tl.x + (i % TW),
                ty = tl.y + Math.floor(i / TW);
            try {
                const r = await axios.get(
                    `https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/${zoom}/${ty}/${tx}`, {
                        responseType: 'arraybuffer',
                        timeout: 30000
                    }
                );
                return {
                    tx,
                    ty,
                    buf: Buffer.from(r.data)
                };
            } catch {
                return null;
            }
        })
    );

    const mosaic = new Jimp(TW * S, TH * S, 0x000000ff);
    for (const t of tileResults.filter(Boolean))
        mosaic.composite(await Jimp.read(t.buf), (t.tx - tl.x) * S, (t.ty - tl.y) * S);

    const mW = TW * S,
        mH = TH * S;
    const cropX = Math.round((west - tile2lng(tl.x, zoom)) / (tile2lng(br.x + 1, zoom) - tile2lng(tl.x, zoom)) * mW);
    const cropY = Math.round((tile2lat(tl.y, zoom) - north) / (tile2lat(tl.y, zoom) - tile2lat(br.y + 1, zoom)) * mH);
    const cropW = Math.min(Math.round((east - west) / (tile2lng(br.x + 1, zoom) - tile2lng(tl.x, zoom)) * mW), mW - Math.max(0, cropX));
    const cropH = Math.min(Math.round((north - south) / (tile2lat(tl.y, zoom) - tile2lat(br.y + 1, zoom)) * mH), mH - Math.max(0, cropY));

    const texPath = path.join(outputDir, 'texture.png');
    await mosaic.crop(Math.max(0, cropX), Math.max(0, cropY), cropW, cropH).resize(res, res).writeAsync(texPath);
    console.log(`[Текстура TEXT] Текстура сохранена`);
    return texPath;
}

async function runProcess({
    bbox,
    apiKey,
    resolution = 513,
    outputDir
}) {
    fs.mkdirSync(outputDir, {
        recursive: true
    });
    console.log('\n=== СТАРТ ПОЛУЧЕНИЯ ДАННЫХ ===');
    const tifPath = await downloadDEM(bbox, apiKey, outputDir);
    const {
        rawPath,
        previewPath,
        min,
        max
    } = await convertDEM(tifPath, resolution, outputDir);
    const texPath = await downloadTexture(bbox, resolution, outputDir);
    console.log('=== ДАННЫЕ ПОЛУЧЕНЫ ===\n');
    return {
        files: {
            tif: tifPath,
            raw: rawPath,
            texture: texPath,
            preview: previewPath
        },
        meta: {
            bbox,
            resolution,
            heightMin: min,
            heightMax: max
        }
    };
}

module.exports = {
    runProcess
};
