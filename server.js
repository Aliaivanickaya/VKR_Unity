require('dotenv').config();

const express = require('express');
const path = require('path');
const fs = require('fs');
const archiver = require('archiver');
const {
    runProcess
} = require('./process');

const app = express();
const PORT = process.env.PORT || 3000;

app.use(express.json());
app.use(express.static(path.join(__dirname, 'public')));

const OUTPUT_DIR = path.join(__dirname, 'output');
fs.mkdirSync(OUTPUT_DIR, {
    recursive: true
});

app.post('/api/generate', async (req, res) => {
    const {
        bbox,
        resolution = 513
    } = req.body;
    const apiKey = process.env.OPENTOPO_API_KEY;

    if (!apiKey || apiKey === 'your_api_key_here') {
        return res.status(500).json({
            error: 'OPENTOPO_API_KEY не задан в .env'
        });
    }
    if (!bbox || bbox.north == null || bbox.south == null || bbox.east == null || bbox.west == null) {
        return res.status(400).json({
            error: 'Некорректный bbox'
        });
    }
    if ((bbox.north - bbox.south) > 1 || (bbox.east - bbox.west) > 1) {
        return res.status(400).json({
            error: 'Участок слишком большой (макс. 1°×1°)'
        });
    }

    try {
        const jobId = Date.now().toString();
        const jobDir = path.join(OUTPUT_DIR, jobId);
        const result = await runProcess({
            bbox,
            apiKey,
            resolution,
            outputDir: jobDir
        });

        res.json({
            jobId,
            meta: result.meta,
            downloadUrl: `/api/download/${jobId}`,
        });
    } catch (err) {
        console.error(err.message);
        res.status(500).json({
            error: err.message
        });
    }
});

app.get('/api/download/:jobId', (req, res) => {
    const jobDir = path.join(OUTPUT_DIR, req.params.jobId);
    if (!fs.existsSync(jobDir)) return res.status(404).send('Not found');

    res.setHeader('Content-Type', 'application/zip');
    res.setHeader('Content-Disposition', 'attachment; filename="terrain_assets.zip"');

    const archive = archiver('zip');
    archive.pipe(res);
    for (const f of ['heightmap.tif', 'height.raw', 'texture.png', 'heightmap_preview.png']) {
        const fp = path.join(jobDir, f);
        if (fs.existsSync(fp)) archive.file(fp, {
            name: f
        });
    }
    archive.finalize();
});

app.get('/api/preview/:jobId/:type', (req, res) => {
    const name = req.params.type === 'texture' ? 'texture.png' : 'heightmap_preview.png';
    const fp = path.join(OUTPUT_DIR, req.params.jobId, name);
    if (!fs.existsSync(fp)) return res.status(404).send('Not found');
    res.sendFile(fp);
});

app.listen(PORT, () => {
    console.log(`Сервер запущен: http://localhost:${PORT}`);
});
