"""Render page 1 of a PDF to PNG with pypdfium2: python render_pdf.py <pdf> <png> [dpi]"""
import sys

import pypdfium2 as pdfium

pdf, png = sys.argv[1], sys.argv[2]
dpi = float(sys.argv[3]) if len(sys.argv) > 3 else 150
page = pdfium.PdfDocument(pdf)[0]
page.render(scale=dpi / 72).to_pil().save(png)
print(png)
