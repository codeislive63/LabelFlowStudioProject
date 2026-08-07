import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';

const input = fileURLToPath(
    new URL(
        './src/LabelFlowStudio.Templates/EditorFrontend/monaco-bootstrap.js',
        import.meta.url
    )
);

const outDir = fileURLToPath(
    new URL(
        './src/LabelFlowStudio.Templates/Editor/monaco-esm',
        import.meta.url
    )
);

export default defineConfig({
    base: './',

    build: {
        outDir,
        emptyOutDir: true,
        sourcemap: true,
        target: 'es2022',

        // Собираем Monaco CSS в один файл.
        cssCodeSplit: false,

        rollupOptions: {
            input,

            output: {
                entryFileNames: 'monaco-bootstrap.js',

                chunkFileNames:
                    'assets/[name]-[hash].js',

                assetFileNames: assetInfo => {
                    const names = [
                        assetInfo.name,
                        ...(assetInfo.names ?? [])
                    ].filter(Boolean);

                    if (names.some(name =>
                        name.endsWith('.css'))) {
                        return 'monaco.css';
                    }

                    return 'assets/[name]-[hash][extname]';
                }
            }
        }
    }
});
